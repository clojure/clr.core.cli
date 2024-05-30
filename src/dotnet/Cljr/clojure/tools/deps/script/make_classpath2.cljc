;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns ^{:skip-wiki true}
  clojure.tools.deps.script.make-classpath2
  (:require
    #?(:clj [clojure.java.io :as jio]
	   :cljr [clojure.clr.io :as cio])
    [clojure.pprint :as pprint]
    [clojure.string :as str]
    [clojure.tools.cli :as cli]
    [clojure.tools.deps :as deps]
    [clojure.tools.deps.extensions :as ext]
    [clojure.tools.deps.tool :as tool]
    [clojure.tools.deps.util.io :as io :refer [printerrln]]
    [clojure.tools.deps.script.parse :as parse]
    [clojure.tools.deps.tree :as tree])
  (:import
    [clojure.lang IExceptionInfo]))

(def ^:private opts
  [;; deps.edn inputs
   [nil "--config-user PATH" "User deps.edn location"]
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
    (printerrln "WARNING: Specified aliases are undeclared and are not being used:" (vec unknown))))

(defn resolve-tool-args
  "Resolves the tool by name to the coord + usage data.
   Returns the proper alias args as if the tool was specified as an alias."
  [tool-name config]
  (if-let [{:keys [lib coord]} (tool/resolve-tool tool-name)]
    (let [manifest-type (ext/manifest-type lib coord config)
          coord' (merge coord manifest-type)
          {:keys [ns-default ns-aliases]} (ext/coord-usage lib coord' (:deps/manifest coord') config)]
      {:replace-deps {lib coord'}
       :replace-paths ["."]
       :ns-default ns-default
       :ns-aliases ns-aliases})
    (throw (ex-info (str "Unknown tool: " tool-name) {:tool tool-name}))))

(defn run-core
  "Run make-classpath script from/to data (no file stuff). Returns:
    {;; Outputs:
     :basis        ;; the basis, including classpath roots
     :trace        ;; if requested, trace.edn file
     :manifests    ;; manifest files used in making classpath
    }"
  [{:keys [install-deps user-deps project-deps config-data ;; all deps.edn maps
           tool-mode tool-name tool-resolver ;; -T options
           main-aliases exec-aliases repl-aliases tool-aliases
           skip-cp threads trace tree] :as _opts}]
  (when (and main-aliases exec-aliases)
    (throw (ex-info "-M and -X cannot be used at the same time" {})))
  (let [pretool-edn (deps/merge-edns [install-deps user-deps project-deps config-data])
        ;; tool use - :deps/:paths/:replace-deps/:replace-paths in project if needed
        tool-args (cond
                    tool-name (tool-resolver tool-name pretool-edn)
                    tool-mode {:replace-deps {} :replace-paths ["."]})
        tool-edn (when tool-args {:aliases {:deps/TOOL tool-args}})
        ;; :deps/TOOL is a synthetic deps.edn combining the tool definition and usage
        ;; it is injected at the end of the deps chain and added as a pseudo alias
        ;; the effects are seen in the basis but this pseduo alias should not escape
        combined-tool-args (deps/combine-aliases
                            (deps/merge-edns [pretool-edn tool-edn])
                            (concat main-aliases exec-aliases repl-aliases tool-aliases (when tool-edn [:deps/TOOL])))
        project-deps (deps/tool project-deps combined-tool-args)

        ;; calc basis
        merge-edn (deps/merge-edns [install-deps user-deps project-deps config-data (when tool-edn tool-edn)]) ;; recalc to get updated project-deps
        combined-exec-aliases (concat main-aliases exec-aliases repl-aliases tool-aliases (when tool-edn [:deps/TOOL]))
        _ (check-aliases merge-edn combined-exec-aliases)
        argmap (deps/combine-aliases merge-edn combined-exec-aliases)
        resolve-args (cond-> argmap
                       threads (assoc :threads (#?(:clj Long/parseLong :cljr Int64/Parse) threads))
                       trace (assoc :trace trace)
                       tree (assoc :trace true))
        basis (cond-> nil
               (not skip-cp) (merge
                              (deps/calc-basis merge-edn {:resolve-args resolve-args, :classpath-args argmap})
                              {:basis-config (cond-> {} ;; :root and :project are always :standard
                                               (nil? user-deps) (assoc :user nil) ;; -Srepro => :user nil
                                               config-data (assoc :extra config-data)  ;; -Sdeps => :extra ...
                                               (seq combined-exec-aliases) (assoc :aliases (vec combined-exec-aliases)))})
                (pos? (count argmap)) (assoc :argmap argmap))
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
    (when (and (-> argmap :main-opts seq) repl-aliases)
      (io/printerrln "WARNING: Use of :main-opts with -A is deprecated. Use -M instead."))
    (cond-> {:basis basis}
      trace (assoc :trace trace)
      manifests (assoc :manifests manifests))))

(defn read-deps
  [name]
  (when (not (str/blank? name))
    (let [f (#?(:clj jio/file :cljr cio/file-info) name)]
      (when (#?(:clj .exists  :cljr .Exists) f)
        (deps/slurp-deps f)))))

(defn write-lines
  [lines file]
  (if lines
    (io/write-file file (apply str (interleave lines (repeat "\n"))))
    (let [jf (#?(:clj jio/file :cljr cio/file-info) file)]
      (when (#?(:clj .exists  :cljr .Exists) jf)
        (#?(:clj .delete :cljr .Delete) jf)))))

(defn run
  "Run make-classpath script. See -main for details."
  [{:keys [install-dir config-user config-project cp-file jvm-file main-file basis-file manifest-file skip-cp trace tree] :as opts}]

  (reset! clojure.tools.deps/install-dir install-dir)

  (let [opts' (merge opts {:install-deps (deps/root-deps)
                           :user-deps (read-deps config-user)
                           :project-deps (or (read-deps "deps-clr.edn") (read-deps config-project))
                           :tool-resolver resolve-tool-args})
        {:keys [basis manifests], trace-log :trace} (run-core opts')
        {:keys [argmap libs classpath-roots]} basis
        {:keys [jvm-opts main-opts]} argmap]
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
    (write-lines manifests manifest-file)))

(defn -main
  "Main entry point for make-classpath script.

  Options:
    --config-user=path - user deps.edn file (usually ~/.clojure/deps.edn)
    --config-project=path - project deps.edn file (usually ./deps.edn)
    --config-data={...} - deps.edn as data (from -Sdeps)
    --tool-mode - flag for tool mode
    --tool-name - name of tool to run
    --cp-file=path - cp cache file to write
    --jvm-file=path - jvm opts file to write
    --main-file=path - main opts file to write
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
      (printerrln "Error building classpath." (#?(:clj .getMessage :cljr .Message) t))
      (when-not (instance? IExceptionInfo t)
        #?(:clj (.printStackTrace t)
		   :cljr  (System.Console/WriteLine (.StackTrace t))))
      (#?(:clj System/exit :cljr Environment/Exit) 1))))