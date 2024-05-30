;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns ^{:skip-wiki true}
  clojure.tools.deps.util.dir
  (:require
    #?(:clj [clojure.java.io :as jio] 
	   :cljr [clojure.clr.io :as cio]))
  (:import
    #?(:clj [java.io File] 
	   :cljr [System.IO Path DirectoryInfo FileInfo FileSystemInfo])
    #?(:clj [java.nio.file Files Path])))

(set! *warn-on-reflection* true)

(def ^:dynamic  ^#?(:clj File :cljr DirectoryInfo) *the-dir*
  "Thread-local directory context for resolving relative directories.
  Defaults to current directory. Should always hold an absolute directory
  java.io.File, never null."
  #?(:clj (jio/file (System/getProperty "user.dir"))
     :cljr (cio/dir-info Environment/CurrentDirectory)))

#?
(:clj
(defn canonicalize
  "Make canonical File in terms of the current directory context.
  f may be either absolute or relative."
  ^File [^File f]
  (.getCanonicalFile
    (if (.isAbsolute f)
      f
      (jio/file *the-dir* f))))
	  
:cljr ;; no equivalent notion of canonical.  A FileInfo is always absolute.
      ;; if relative paths are involved we must be sure to pass a string instead.
(defn canonicalize
  "Make canonical File in terms of the current directory context.
  f may be either absolute or relative."
  ^FileSystemInfo [f]
  (let [f (if (instance? FileSystemInfo f) (.FullName ^FileSystemInfo f) (str f))]
    (if (Path/IsPathRooted f)
       (cio/file-info f)
	   (cio/file-info (Path/Join (.FullName *the-dir*) f)))))
)

(defmacro with-dir
  "Push directory into current directory context for execution of body."
  [^#?(:clj File  :cljr DirectoryInfo) dir & body]
  `(binding [*the-dir* (canonicalize ~dir)]
     ~@body))
#?
(:clj
(defn- same-file?
  "If a file can't be read (most common reason is directory does not exist), then
  treat this as not the same file (ie unknown)."
  [^Path p1 ^Path p2]
  (try
    (Files/isSameFile p1 p2)
    (catch Exception _ false)))
	
:cljr
(defn- same-file?
  [^FileSystemInfo p1 ^FileSystemInfo p2]
  (= (.FullName p1) (.FullName p2)))
)

#?(
:clj
(defn sub-path?
  "True if the path is a sub-path of the current directory context.
  path may be either absolute or relative. Will return true if path
  has a parent that is the current directory context, false otherwise.
  Handles relative paths, .., ., etc. The sub-path does not need to
  exist on disk (but the current directory context must)."
  [^File path]
  (if (nil? path)
    false
    (let [root-path (.toPath ^File *the-dir*)]
      (loop [check-path (.toPath (canonicalize path))]
        (cond
          (nil? check-path) false
          (same-file? root-path check-path) true
          :else (recur (.getParent check-path)))))))
		  
:cljr  ;; not sure yet if this is what is needed.  Need to see it in action.
(defn sub-path?
  [^FileInfo path]
  (if (nil? path)
    false 
    (let [root-path *the-dir*]
      (loop [check-path (.Directory path)]
	    (cond 
		  (nil? check-path) false
		  (same-file? root-path check-path) true
		  :else (recur (.Parent check-path)))))))
)
	  

;; DEPRECATED
#?(
:clj
(defn as-canonical
  ^File [^File dir]
  (canonicalize dir))
  
:cljr
(defn as-canonical
  ^FileSystemInfo [^FileSystemInfo dir]
  (canonicalize dir))
  
)