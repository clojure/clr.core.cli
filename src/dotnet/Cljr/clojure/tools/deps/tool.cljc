;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns clojure.tools.deps.tool
  (:require
    #?(:clj [clojure.java.io :as jio] :cljr [clojure.clr.io :as cio])
    [clojure.pprint :as pprint]
    [clojure.string :as str]
    [clojure.tools.deps :as deps]
    [clojure.tools.deps.extensions :as ext]
    [clojure.tools.deps.util.io :as io])
  (:import
    #?(:clj [java.io File] :cljr [System.IO FileInfo DirectoryInfo])))

#?(
:clj 
(defn- tool-dir
  ^File []
  (jio/file (.getParentFile (jio/file (deps/user-deps-path))) "tools"))

:cljr
(defn- tool-dir 
  ^DirectoryInfo []
  (cio/dir-info (.Parent (cio/dir-info (deps/user-deps-path))) "tools"))
)

#?(
:clj
(defn- tool-file
  "Create File location for tool name"
  ^File [tool]
  (jio/file (tool-dir) (str tool ".edn")))
  
:cljr
(defn- tool-file
  "Create File location for tool name"
  ^FileInfo [tool]
  (cio/file-info (tool-dir) (str tool ".edn")))
)
  

(defn install-tool
  "Procure the lib+coord, install the tool to the user tools dir (with lib, coord)"
  [lib coord as]
  (let [{:keys [root-edn user-edn]} (deps/find-edn-maps)
        master-edn (deps/merge-edns [root-edn user-edn])
        deps-info (ext/manifest-type lib coord master-edn)
        f (tool-file as)]
    ;; procure
    (ext/coord-paths lib (merge coord deps-info) (:deps/manifest deps-info) master-edn)
    ;; ensure tool dir
    #?(:clj (.mkdirs (.getParentFile f))
	   :cljr (.Create (.Directory f)))
    ;; write tool file
    (spit f
      (with-out-str
        (binding [*print-namespace-maps* false
                  pprint/*print-right-margin* 100]
          (pprint/pprint {:lib lib :coord coord})))
      #?@(:cljr (:file-mode System.IO.FileMode/Create)))))

(defn resolve-tool
  "Resolve a tool by name, look up and return:
  {:lib lib
   :coord coord}
  Or nil if unknown."
  [tool]
  (let [f (tool-file tool)]
    (when (#?(:clj .exists :cljr .Exists)f)
      (io/slurp-edn f))))

(defn usage
  "Resolve a tool and return it's usage data, which may be nil.
  Throws ex-info if tool is unknown."
  [tool]
  (if-let [{:keys [lib coord]} (resolve-tool tool)]
    (let [{:keys [root-edn user-edn]} (deps/find-edn-maps)
          config (deps/merge-edns [root-edn user-edn])
          [lib coord] (ext/canonicalize lib coord config)
          manifest-type (ext/manifest-type lib coord config)]
      (ext/coord-usage lib (merge coord manifest-type) (:deps/manifest manifest-type) config))
    (throw (ex-info (str "Unknown tool: " tool) {:tool tool}))))

(defn list-tools
  "Return seq of available tool names"
  []
  (->> (#?(:clj .listFiles :cljr .GetFiles) (tool-dir))
    #?(:clj (filter #(.isFile ^File %)))
    #?(:clj (map #(.getName ^File %))
	   :cljr (map #(.Name ^FileInfo %)))
    (filter #(str/ends-with? % ".edn"))
    (map #(subs % 0 (- (count %) 4)))
    sort))

(defn remove-tool
  "Removes tool installation, if it exists. Returns true if it exists and was deleted."
  [tool]
  (let [f (tool-file tool)]
    (when (#?(:clj .exists :cljr .Exists) f)
      (#?(:clj .delete :cljf .Delete) f))))