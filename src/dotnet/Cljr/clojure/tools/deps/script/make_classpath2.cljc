;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns ^{:skip-wiki true}
  clojure.tools.deps.script.make-classpath2
  "Implements the Clojure CLI dep expansion using tools.deps.
  IMPL namespace, subject to change without warning.
  Likely to move to CLI code base in the future."
  (:require
    #?(:clj [clojure.java.io :as jio]
	   :cljr [clojure.clr.io :as cio])
    [clojure.pprint :as pprint]
    [clojure.tools.cli :as cli]
    [clojure.tools.deps :as deps]
    [clojure.tools.deps.extensions :as ext]
    [clojure.tools.deps.tool :as tool]
    [clojure.tools.deps.util.io :as io]
    [clojure.tools.deps.script.parse :as parse]
    [clojure.tools.deps.tree :as tree])
  (:import
    [clojure.lang IExceptionInfo]))

(defn blank-to-nil [s]
  (if (zero? (count s)) nil s))

(def ^:private opts
  [;; deps.edn inputs
   [nil "--config-user PATH" "User deps.edn location" :parse-fn blank-to-nil]
   [nil "--config-project PATH" "Project deps.edn location"]
   [nil "--config-data EDN" "Final deps.edn data to treat as the last deps.edn file" :parse-fn parse/parse-config]
   #?(:cljr [nil "--install-dir PATH" "Installation directory (CLJR only), internal use"] )
   ;; tool args to resolve
   [nil "--tool-mode" "Tool mode (-T), may optionally supply tool-name or tool-aliases"]
   [nil "--tool-name NAME" "Tool name"]
   ;; output files
   [nil "--cp-file PATH" "Classpatch cache file to write"]
   [nil "--jvm-file PATH" "JVM options file"]
   [nil "--main-file PATH" "Main options file"]
   #?(:cljr [nil "--frameworks-file PATH" ".NET frameworks file"])
   #?(:cljr [nil "--clojure-version-file PATH" "Requested Clojure.Main version"])
   [nil "--manifest-file PATH" "Manifest list file"]
   [nil "--basis-file PATH" "Basis file"]
   [nil "--skip-cp" "Skip writing .cp files"]
   ;; aliases
   ["-A" "--repl-aliases ALIASES" "Concatenated repl alias names" :parse-fn parse/parse-kws]
   ["-M" "--main-aliases ALIASES" "Concatenated main option alias names" :parse-fn parse/parse-kws]
   ["-X" "--exec-aliases ALIASES" "Concatenated exec alias names" :parse-fn parse/parse-kws]
   ["-T" "--tool-aliases ALIASES" "Concatenated tool alias names" :parse-fn parse/parse-kws]
   ;; options
   [nil "--trace" "Emit trace log to trace.edn"]
   [nil "--threads THREADS" "Threads for concurrent downloads"]
   [nil "--tree" "Print deps tree to console"]])

(defn parse-opts
  "Parse the command line opts to make-classpath"
  [args]
  (cli/parse-opts args opts))

(defn check-aliases
  "Check that all aliases are known and warn if aliases are undeclared"
  [deps aliases]
  (when-let [unknown (seq (remove #(contains? (:aliases deps) %) (distinct aliases)))]
    (io/printerrln "WARNING: Specified aliases are undeclared and are not being used:" (vec unknown))))

(defn resolve-tool-args
  "Resolves the tool by name to the coord + usage data.
   Returns the proper alias args as if the tool was specified as an alias."
  [tool-name]
  (if-let [{:keys [lib coord]} (tool/resolve-tool tool-name)]
    (let [config nil ;; for now, tools are only git or local which have none
          manifest-type (ext/manifest-type lib coord config)
          coord' (merge coord manifest-type)
          {:keys [ns-default ns-aliases]} (ext/coord-usage lib coord' (:deps/manifest coord') config)]
      {:replace-deps {lib coord'}
       :replace-paths ["."]
       :ns-default ns-default
       :ns-aliases ns-aliases})
    (throw (ex-info (str "Unknown tool: " tool-name) {:tool tool-name}))))

(defn run-core
  "Run make-classpath script from/to data. Returns map w/keys:
     :basis        ;; the basis, including classpath roots
     :trace        ;; if requested, trace.edn file
     :manifests    ;; manifest files used in making classpath"
  [{:keys [config-user config-project config-data tool-data
           main-aliases exec-aliases repl-aliases tool-aliases
           skip-cp threads trace tree] :as _opts}]
  (when (and main-aliases exec-aliases)
    (throw (ex-info "-M and -X cannot be used at the same time" {})))
  (when (and (string? config-data) (not #?(:clj (.exists (jio/file config-data))  :cljr (.Exists (cio/file-info config-data)))))
    (throw (ex-info (str "Extra deps file not found: " config-data) {})))	
  (let [combined-exec-aliases (concat main-aliases exec-aliases repl-aliases tool-aliases)
        args (cond-> nil
               threads (assoc :threads (#?(:clj Long/parseLong :cljr Int64/Parse) threads))
               trace (assoc :trace trace)
               tree (assoc :trace true)
               skip-cp (assoc :skip-cp true)
               (or tool-data tool-aliases) (assoc :replace-paths ["."] :replace-deps {})
               tool-data (merge tool-data))
        basis (deps/create-basis
                {:user config-user
                 :project config-project
                 :extra config-data
                 :aliases combined-exec-aliases
                 :args args})

        ;; check for unused aliases and warn
        _ (check-aliases basis combined-exec-aliases)

        libs (:libs basis)
        trace (-> libs meta :trace)

        ;; check for unprepped libs
        _ (deps/prep-libs! libs {:action :error} basis)

        ;; determine manifest files to add to cache check
        manifests (->>
                   (for [[lib coord] libs]
                     (let [mf (ext/manifest-type lib coord basis)]
                       (ext/manifest-file lib coord (:deps/manifest mf) basis)))
                   (remove nil?)
                   seq)]
    (when (and (-> (:argmap basis) :main-opts seq) repl-aliases)
      (io/printerrln "WARNING: Use of :main-opts with -A is deprecated. Use -M instead."))
    (cond-> {:basis basis}
      trace (assoc :trace trace)
      manifests (assoc :manifests manifests))))

(defn write-lines
  [lines file]
  (if lines
    (io/write-file file (apply str (interleave lines (repeat "\n"))))
    (let [jf (#?(:clj jio/file :cljr cio/file-info) file)]
      (when (#?(:clj .exists  :cljr .Exists) jf)
        (#?(:clj .delete :cljr .Delete) jf)))))

;; Determine the alias that is the source of the :clr/clojure-version value
;; Walk aliases in the order they were requested, keep the last one whose map has the key
;; A picture might help.  Here are the relevant parts of basis file:
;;
;;  :basis-config {:project "deps.edn"
;;                 :user    "~/.clojure/deps-clr.edn"
;;                 :extra   {:aliases {:cli {:clr/clojure-version "1.12.5"}}}
;;                 :args    nil
;;                 :aliases [:old :other :new :cli]}          ; <- requested, in order
;;  
;;  :aliases {:old   {:clr/clojure-version "1.12.3-alpha3"}   ; <- every defined alias,
;;            :other {:extra-paths ["y"]}                      ;    name -> definition
;;            :new   {:clr/clojure-version "1.13.0-alpha8", :extra-paths ["x"]}
;;            :cli   {:clr/clojure-version "1.12.5"}           ;    (from -Sdeps)
;;            :test  {:extra-paths ["test"]}                   ;    (defined, not requested)
;;            :deps  {:replace-deps {...}, ...}}               ;    (from the root deps.edn)
;;  
;;  :argmap {:clr/clojure-version "1.12.5", :extra-paths ["y" "x"]}
;;  
;;  :clr/clojure-version "1.12.6"                              ; <- the top-level value
;;  
;;  We have a value for :clr/clojure-version in :argmap.  But we don't know the source, so we must compute it.
;;  The value of (:aliases (:basis-config basis)) is a vector of the aliases considered, in order.
;;  For each alias in order -- :old, :other, :new, :cli -- we check if its map in (:aliases basis) has a value for :clr/clojure-version.
;;  We want the last alias that has a value.  In our example:  :old - yes;  :other - no; :new - yes; :cli - yes.
;;  So our answer is :cli.

(defn determine-clr-version-source [basis]
  (let [config-aliases (:aliases (:basis-config basis))
        defining (filter #(contains? (get-in basis [:aliases %]) :clr/clojure-version) config-aliases)]
    (last defining)))  

(defn determine-clr-version-and-source [argmap-version argmap basis]
  (cond
    argmap-version
    [argmap-version (str "alias " (determine-clr-version-source basis))]
    
    (and (not (contains? argmap :replace-deps))
         (not (contains? argmap :deps))
         (:clr/clojure-version basis))
    [(:clr/clojure-version basis) "top-level"]
     
    :else
    [nil nil]))
    
    
(def ^:private modified-semver-pattern
  (re-pattern
   (str "^"
        "([0-9]+)"                                 ; major  - We allow leading zeros, else should be (0|[1-9]\\d*)
        "\\."
        "([0-9]+)"                                 ; minor  - We allow leading zeros
        "\\."
        "([0-9]+)"                                 ; patch  - We allow leading zeros
        "(?:-"                                        ; optional prelease indicated by hyphen
          "(?:"
            "(?:[0-9A-Za-z]+)"
            "(?:[.-](?:[0-9A-Za-z]+))*"
          ")"
        ")?"
        "(?:\\+"                                      ; optional metadata indicated by +
          "[0-9A-Za-z.-]+"
        ")?"
        "\\z")))
        
(defn valid-version? [v]
  (boolean (and (string? v) (re-matches modified-semver-pattern v))))
  

(defn run
  "Run make-classpath script. See -main for details."
  [{:keys [cp-file jvm-file main-file frameworks-file clojure-version-file basis-file manifest-file skip-cp tool-mode tool-name tool-aliases trace tree] :as opts}]
  #_(when-let [user-agent (some-> "clojure/install/useragent.txt" jio/resource slurp)]   ;;; CLR: Maven specific -- ignore
    (System/setProperty "aether.connector.userAgent" user-agent))  
  (let [opts' (cond-> opts
                (and tool-mode (not tool-aliases))
                (assoc :tool-data (when tool-name (resolve-tool-args tool-name))))
        {:keys [basis manifests], trace-log :trace} (run-core opts')
        {:keys [argmap libs classpath-roots]} basis
        {:keys [jvm-opts main-opts]} argmap
        #?@(:cljr [{:clr/keys [frameworks clojure-version]} argmap])
        #?@(:cljr [[clr-version clr-version-source] (determine-clr-version-and-source clojure-version argmap basis)])]
    #?(:cljr (when (and clr-version (not (valid-version? clr-version)))
               (throw (ex-info
                        (str "Invalid :clr/clojure-version " (pr-str clr-version) " from " clr-version-source) 
                        {:clr/clojure-version clr-version :source clr-version-source}))))
    (when trace
      (spit "trace.edn" 
            (binding [*print-namespace-maps* false] (with-out-str (pprint/pprint trace-log)))
             #?@(:cljr (:file-mode System.IO.FileMode/Create))))
    (when tree
      (-> trace-log tree/trace->tree (tree/print-tree nil)))
    (when-not skip-cp
      (io/write-file cp-file (-> classpath-roots deps/join-classpath)))
    (io/write-file basis-file (binding [*print-namespace-maps* false] (pr-str basis)))
    (write-lines (seq jvm-opts) jvm-file)
    (write-lines (seq main-opts) main-file)  ;; FUTURE: add check to only do this if main-aliases were passed
    #?(:cljr (when frameworks-file (write-lines (seq frameworks) frameworks-file)))
    #?(:cljr (when clojure-version-file 
               (write-lines (if clr-version [clr-version clr-version-source] nil) clojure-version-file)))
    (write-lines manifests manifest-file)))

(defn -main
  "Main entry point for make-classpath script.

  Options:
    --config-user=path - user deps.edn file (usually ~/.clojure/deps.edn)
    --config-project=path - project deps.edn file (usually ./deps.edn)
    --config-data={...} - deps.edn as data, or file name (from -Sdeps)
    --tool-mode - flag for tool mode
    --tool-name - name of tool to run
    --cp-file=path - cp cache file to write
    --jvm-file=path - jvm opts file to write
    --main-file=path - main opts file to write
    --frameworks-file=path - .NET frameworks (CLR only)
    --clojure-version-file=path - requested Clojure.Main version (CLR only)
    --manifest-file=path - manifest list file to write
    --basis-file=path - basis file to write
    -Mmain-aliases - concatenated main-opt alias names
    -Aaliases - concatenated repl alias names
    -Xaliases - concatenated exec alias names
    -Taliases - concatenated tool alias names

  Resolves the dependencies and updates the lib, classpath, etc files.
  The cp file is at <cachedir>/<hash>.cp
  The main opts file is at <cachedir>/<hash>.main (if needed)
  The jvm opts file is at <cachedir>/<hash>.jvm (if needed)
  The manifest file is at <cachedir>/<hash>.manifest (if needed)"
  [& args]
  (try
    (let [{:keys [options errors]} (parse-opts args)]
      (when (seq errors)
        (run! println errors)
        (#?(:clj System/exit :cljr Environment/Exit) 1))
      (run options))
    (catch #?(:clj Throwable :cljr Exception) t
      (io/printerrln "Error building classpath." (#?(:clj .getMessage :cljr .Message) t))
      (when-not (instance? IExceptionInfo t)
        #?(:clj (.printStackTrace t)
		   :cljr  (System.Console/WriteLine (.StackTrace t))))
      (#?(:clj System/exit :cljr Environment/Exit) 1))))