;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns clojure.tools.deps.cli.api
  "This api provides functions that can be executed from the Clojure tools using -X:deps."
  (:refer-clojure :exclude [list])
  (:require
    [clojure.edn :as edn]
    #?(:clj [clojure.java.io :as jio]
	   :cljr [clojure.clr.io :as cio])
    [clojure.pprint :as pprint]
    [clojure.string :as str]
    [clojure.tools.deps :as deps]
    [clojure.tools.deps.tool :as tool]
    [clojure.tools.deps.tree :as tree]
    [clojure.tools.deps.script.resolve-tags :as resolve-tags]
    [clojure.tools.deps.extensions :as ext]
    #?(:clj [clojure.tools.deps.extensions.pom :as pom])
    [clojure.tools.deps.extensions.local :as local]
    #?(:clj [clojure.tools.deps.gen.pom :as gen-pom])
    #?(:clj [clojure.tools.deps.util.maven :as mvn])
    [clojure.tools.deps.util.io :as io :refer [printerrln]]
    [clojure.set :as set])
  (:import
    #?(:clj [java.io File FileNotFoundException IOException])
    #?(:clj [java.nio.file Files])
    #?(:clj [java.nio.file.attribute FileAttribute])
    #?(:clj [java.util.jar JarFile])
    #?(:clj [org.apache.maven.model Model])
    #?(:clj [org.eclipse.aether.artifact DefaultArtifact])
    #?(:clj [org.eclipse.aether.installation InstallRequest])
    [clojure.lang IExceptionInfo]))

(set! *warn-on-reflection* true)

(defn prep
  "Prep the unprepped libs found in the transitive lib set of basis.

  This program accepts the same basis-modifying arguments from the `basis` program.
  Each dep source value can be :standard, a string path, a deps edn map, or nil.
  Sources are merged in the order - :root, :user, :project, :extra.

  Options:
    :force - flag on whether to force prepped libs to re-prep (default = false)
    :current - flag on whether to prep current project too (default = false)
    :log - :none, :info (default), or :debug

  Basis options:
    :root    - dep source, default = :standard
    :user    - dep source, default = :standard
    :project - dep source, default = :standard (\"./deps.edn\")
    :extra   - dep source, default = nil
    :aliases - coll of kw aliases of argmaps to apply to subprocesses

  Returns params used."
  [{:keys [force log current] :or {log :info, current false} :as params}]
  (let [basis (deps/create-basis params)
        opts {:action (if force :force :prep)
              :log log
              :current current}]
    (deps/prep-libs! (:libs basis) opts basis)
    params))

(comment
  (do
    (prep
      {:root {:mvn/repos mvn/standard-repos, :deps nil}
       :project {:deps '{org.clojure/clojure {:mvn/version "1.10.3"}
                         io.github.puredanger/cool-lib
                         {:git/sha "657d5ce88be340ab2a6c0befeae998366105be84"}}}
       :log :debug
       :force true})
    nil)
  )

(defn tree
  "Print deps tree for the current project's deps.edn built from either the
  a basis, or if provided, the trace file.

  This program accepts the same basis-modifying arguments from the `basis` program.
  Each dep source value can be :standard, a string path, a deps edn map, or nil.
  Sources are merged in the order - :root, :user, :project, :extra.

  By default, :format will :print to the console in a human friendly tree. Use
  :edn mode to print the tree to edn.

  In print mode, deps are printed with prefix of either . (included) or X (excluded).
  A reason code for inclusion/exclusion may be added at the end of the line.

  Basis options:
    :root    - dep source, default = :standard
    :user    - dep source, default = :standard
    :project - dep source, default = :standard (\"./deps.edn\")
    :extra   - dep source, default = nil
    :aliases - coll of kw aliases of argmaps to apply to subprocesses

  Input options (if provided, basis options ignored):
    :file      Path to trace.edn file (from clj -Strace) to use in computing the tree

  Output mode:
    :format    :print (default) or :edn

  Print output mode modifiers:
    :indent    Indent spacing (default = 2)
    :hide-libs Set of libs to hide as deps (if not top dep), default = #{org.clojure/clojure}"
  [opts]
  (try
    (let [{:keys [file format] :or {format :print}} opts
          trace (if file
                  (io/slurp-edn file)
                  (tree/calc-trace opts))
          tree (tree/trace->tree trace)]
      (case format
        :print (tree/print-tree tree opts)
        :edn (pprint/pprint tree)
        (throw (ex-info (str "Unknown format " format) {}))))
    (catch #?(:clj Throwable :cljr Exception) t
      (printerrln "Error generating tree:" (#?(:clj .getMessage :cljr .Message) t))
      (when-not (instance? IExceptionInfo t)
        #?(:clj (.printStackTrace t)
		   :cljr (System.Console/WriteLine (.StackTrace t))))
      (#?(:clj System/exit :cljr Environment/Exit) 1))))

(comment
  (tree nil)
  (tree {:extra {:aliases {:foo {:extra-deps {'criterium/criterium {:mvn/version "0.4.0"}}}}}
         :aliases [:foo]})
  )

(def ^:private cli-alias-keys
  #{:deps :replace-deps :extra-deps :override-deps :default-deps
    :paths :replace-paths :extra-paths :classpath-overrides
    :exec-fn :exec-args :ns-default :ns-aliases
    :main-opts :jvm-opts})

(defn aliases
  "List all aliases available for use with the CLI using -M, -X or -T execution
  (note that some aliases may be usable with more than one of these). Also, the
  deps.edn sources of the alias are specified.

  This program accepts the same basis-modifying arguments from the `basis` program.
  Each dep source value can be :standard, a string path, a deps edn map, or nil.
  Sources are merged in the order - :root, :user, :project, :extra.

  For example, to print only aliases defined in this project:
    clj -X:deps aliases :root nil :user nil

  Basis options:
    :root    - dep source, default = :standard
    :user    - dep source, default = :standard
    :project - dep source, default = :standard (\"./deps.edn\")
    :extra   - dep source, default = nil

  The aliases are printed to the console."
  [params]
  (let [edn-srcs (deps/create-edn-maps params)
        src-aliases (reduce-kv #(assoc %1 %2 (:aliases %3)) {} edn-srcs)
        cli-aliases (reduce-kv
                      (fn [m src aliases]
                        (assoc m
                          src
                          (reduce-kv
                            (fn [a alias alias-defn]
                              (cond-> a
                                (pos? (count (set/intersection cli-alias-keys (set (keys alias-defn)))))
                                (assoc alias alias-defn)))
                            {} aliases)))
                      {} src-aliases)
        all-aliases (->> cli-aliases (map val) (mapcat #(-> % keys sort)) distinct)]
    (doseq [alias all-aliases]
      (let [srcs (reduce-kv (fn [srcs src deps-edn]
                              (if (contains? (:aliases deps-edn) alias)
                                (conj srcs src)
                                srcs))
                   [] edn-srcs)]
        (println alias (str "(" (str/join ", " (map name srcs)) ")"))))))

#?(
:clj
(def ^:private license-abbrev
  (delay
    (-> "clojure/tools/deps/license-abbrev.edn" jio/resource slurp edn/read-string)))
	
:cljr
(def ^:private license-abbrev
  (delay  
    (letfn [(find-file [x] (clojure.lang.RT/FindFile x))]
      (-> "clojure/tools/deps/license-abbrev.edn" find-file slurp edn/read-string))))
)

(defn- license-string
  [info license-mode]
  (let [abbrevs @license-abbrev
        license-name (when (#{:full :short} license-mode) (:name info))]
    (if (and license-name (= license-mode :short))
      (get abbrevs license-name license-name)
      license-name)))

(defn list
  "List all deps on the classpath, optimized for knowing the final set of included
  libs. The `tree` program can provide more info on why or why not a particular
  lib is included.

  Licenses will be printed in short form by default but can also be listed as
  in :full or :none for none at all using the :license key.

  By default, :format will :print to the console in a human friendly tree. Use
  :edn mode to print the tree to edn.

  This program accepts the same basis-modifying arguments from the `basis` program.
  Each dep source value can be :standard, a string path, a deps edn map, or nil.
  Sources are merged in the order - :root, :user, :project, :extra.

  Options:
    :license - :full, :short (default), :none

  Output mode options:
    :format    :print (default) or :edn

  Basis options:
    :root    - dep source, default = :standard
    :user    - dep source, default = :standard
    :project - dep source, default = :standard (\"./deps.edn\")
    :extra   - dep source, default = nil
    :aliases - coll of kw aliases of argmaps to apply to subprocesses

  The libs are printed to the console."
  [params]
  (let [{license-mode :license, format :format
         :or {license-mode :short, format :print}} params
        basis (deps/create-basis params)
        libs (:libs basis)
        data (into (sorted-map)
               (map (fn [lib]
                      (let [coord (get libs lib)
                            info (ext/license-info lib coord basis)]
                        [lib (cond-> coord info (assoc :license info))]))
                 (-> libs keys sort)))]
    (if (= format :edn)
      (binding [*print-namespace-maps* false]
        (pprint/pprint data))
      (doseq [[lib coord] data]
        (let [summary (ext/coord-summary lib coord)
              info (:license coord)
              license-string (license-string info license-mode)]
          (println summary (if license-string (str " (" license-string ")") "")))))))

(comment
  (list nil)
  @license-abbrev
  )

;;;; git resolve-tags

(defn git-resolve-tags
  "Resolve git tags in deps.edn git deps to full shas."
  [_]
  (resolve-tags/exec {:deps-file "deps.edn"}))

;;;; Generate pom

#?(
:clj 
(defn mvn-pom
  "Sync or create pom.xml from deps.edn.

  This program accepts the same basis-modifying arguments from the `basis` program.
  Each dep source value can be :standard, a string path, a deps edn map, or nil.
  Sources are merged in the order - :root, :user, :project, :extra.

  Basis options:
    :root    - dep source, default = :standard
    :user    - dep source, default = :standard
    :project - dep source, default = :standard (\"./deps.edn\")
    :extra   - dep source, default = nil
    :aliases - coll of kw aliases of argmaps to apply to subprocesses

  Deprecated options (use the basis :aliases above instead):
    :argmaps - vector of aliases to combine into argmaps to resolve-deps and make-classpath"
  [{:keys [argmaps] :as opts}]
  (try
    (let [opts' (if argmaps (assoc opts :aliases (vec (concat argmaps (:aliases opts)))) opts)
          basis (deps/create-basis opts')
          ;; treat all transitive deps as top-level deps
          updated-deps (reduce-kv (fn [m lib {:keys [dependents] :as coord}]
                                    (if (seq dependents) m (assoc m lib coord)))
                         {} (:libs basis))]
      (gen-pom/sync-pom (merge basis {:deps updated-deps}) (jio/file ".")))
    (catch Throwable t
      (printerrln "Error generating pom manifest:" (.getMessage t))
      (when-not (instance? IExceptionInfo t)
        (.printStackTrace t))
      (System/exit 1))))
)

;;;; Install jar into local repository
#?(
:clj 
(defn- read-pom-file
  [pom]
  (let [pom-file (jio/file pom)]
    (if (.exists pom-file)
      (let [^Model model (pom/read-model-file pom-file (deps/root-deps))]
        {:group-id (.getGroupId model)
         :artifact-id (.getArtifactId model)
         :version (.getVersion model)
         :pom-file pom})
      (throw (FileNotFoundException. (str "Pom file not found: " (str pom)))))))
)

#?(
:clj
(defn- gen-pom-file
  [lib version classifier]
  (let [group-id (namespace lib)
        artifact-id (name lib)
        temp-dir (.toString (Files/createTempDirectory "pom" (make-array FileAttribute 0)))
        pom-file (str temp-dir "/pom.xml")]
    (gen-pom/sync-pom {:params {:target-dir temp-dir
                                :src-pom pom-file
                                :lib lib
                                :version version}})
    {:group-id group-id
     :artifact-id artifact-id
     :version version
     :classifier classifier
     :pom-file pom-file}))
)

#?(
:clj
(defn- read-pom-in-jar
  [jar-name]
  (let [jar-file (jio/file jar-name)]
    (when (or (nil? jar-name) (not (.exists jar-file)))
      (throw (FileNotFoundException. (str "Jar file not found: " jar-name))))
    (let [jar (JarFile. jar-file)]
      (if-let [path (local/find-pom jar)]
        (let [entry (.getJarEntry jar path)
              jis (.getInputStream jar entry)
              tmp (File/createTempFile "pom" ".xml")]
          (jio/copy jis tmp)
          (read-pom-file tmp))
        (throw (IOException. (str "Jar file does not contain pom: " jar-name)))))))
)

#?(
:clj
(defn- output-path
  [local-repo group-id artifact-id version]
  (let [path-parts (concat
                     [(or local-repo @mvn/cached-local-repo)]
                     (str/split group-id #"\.")
                     [artifact-id version])]
    (.getAbsolutePath ^File (apply jio/file path-parts))))
)

#?(
:clj 

(defn mvn-install
  "Install a jar and pom to the Maven local cache.
  The pom file must either be supplied, or generated based
  on provided lib/version/classifier, or provided inside the jar.
  The group/artifact/version coordinate will be pulled from the
  pom source as above.

  Required:
    :jar (reqired) - path to jar file (embedded pom used by default)

  Explicit pom options:
    :pom - path to pom file (pom in jar ignored)

  Generated pom options:
    :lib - qualified symbol like my.org/lib
    :version - string
    :classifier - string

  Other options:
    :local-repo (optional) - path to local repo (default = ~/.m2/repository)

  Execute ad-hoc:
    clj -X:deps mvn/install :jar '\"foo-1.2.3.jar\"'"
  [{:keys [jar pom lib version classifier local-repo]}]
  (println "Installing" jar (if pom (str "and " pom) ""))
  (let [{:keys [pom-file group-id artifact-id version classifier]}
        (cond
          pom (read-pom-file pom)
          lib (gen-pom-file lib version classifier)
          :else (read-pom-in-jar jar))
        jar-file (jio/file jar)
        pom-file (jio/file pom-file)
        system (mvn/make-system)
        settings (mvn/get-settings)
        session (mvn/make-session system settings (or local-repo @mvn/cached-local-repo))
        artifacts [(.setFile (DefaultArtifact. group-id artifact-id classifier "jar" version) jar-file)
                   (.setFile (DefaultArtifact. group-id artifact-id classifier "pom" version) pom-file)]
        install-request (.setArtifacts (InstallRequest.) artifacts)]
    (.install system session install-request)
    (println "Installed to" (output-path local-repo group-id artifact-id version))))
)

(defn find-versions
  "Find available tool versions given either a lib (with :lib) or
  existing installed tool (with :tool). If lib, check all registered
  procurers and print one coordinate per line when found.

  Options:
    :lib  Qualified lib symbol
    :tool Tool name for installed tool
    :n    Number of coordinates to return, default = 8, :all for all"
  [{:keys [lib tool n] :or {n 8} :as args}]
  (let [{:keys [root-edn user-edn]} (deps/find-edn-maps)
        master-edn (deps/merge-edns [root-edn user-edn])
        trunc-fn (if (= n :all) (fn [_n x] x) #(apply take-last %&))
        coords (cond
                 tool
                 (if-let [{:keys [lib coord]} (tool/resolve-tool (name tool))]
                   (ext/find-versions lib coord (ext/coord-type coord) master-edn)
                   (throw (ex-info (str "Unknown tool: " tool) {:tool tool})))

                 lib
                 (ext/find-all-versions lib {} master-edn)

                 :else
                 (throw (ex-info "Either :lib or :tool must be provided to find versions" (or args {}))))]
    (run! #(binding [*print-namespace-maps* false] (prn %)) (trunc-fn n coords))))

(comment
  (find-versions '{:lib org.clojure/tools.gitlibs :n 4})
  (find-versions '{:lib io.github.clojure/tools.gitlibs :n 10})
  (find-versions '{:tool tools :n 3})
  (find-versions nil)
  )