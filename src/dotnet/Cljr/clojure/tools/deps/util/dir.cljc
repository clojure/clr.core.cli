;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns
  clojure.tools.deps.util.dir
  "An abstraction for managing a 'current directory' stack.
  The dynamic var `*the-dir*` holds the current directory.
  Use `with-dir` to push a new absolute (or more usefully)
  relative directory onto the stack, which will pop when the
  scope exits. Use `canonical` to get the canonical File
  of a path in terms of the current directory context."
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

;; As convenient a place as any.  Needed in other places.

#?(
:cljr

(compile-when (>= (:major dotnet-version) 5)

(defn trim-ending-directory-separator [^String s]
  (System.IO.Path/TrimEndingDirectorySeparator s))
  
) ;; compile-when
)


#?(
:cljr

(defn directory-separator-char? [c]
 (or (= System.IO.Path/DirectorySeparatorChar c)
	 (= System.IO.Path/AltDirectorySeparatorChar c)))
)

#?(
:cljr

(compile-when (< (:major dotnet-version) 5)



(defn trim-ending-directory-separator [^String s]
  (cond
     (String/IsNullOrWhiteSpace s)  
	 s
     (= (System.IO.Path/GetPathRoot s) s) 
	 s
     (directory-separator-char? (last s)) 
	 (String/.Substring s 0 (dec (String/.Length s)))
	 :else 
	 s))
  
) ;; compile-when
)

#?(
:cljr

(defn resolve-against [^String base ^String path]
  (trim-ending-directory-separator (System.IO.Path/GetFullPath (System.IO.Path/Combine base path))))

)

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
    (cio/file-info (resolve-against (.FullName *the-dir*) f))))

)

#?(
:clj

(defmacro with-dir
  "Push directory into current directory context for execution of body."
  [^File dir & body]
  `(binding [*the-dir* (canonicalize ~dir)]
     ~@body))


:cljr

(defmacro with-dir
  "Push directory into current directory context for execution of body."
  [^DirectoryInfo dir & body]
  `(binding [*the-dir* (cio/dir-info (.FullName (canonicalize ~dir)))]
     ~@body))
	 
)	 
	 
#?(
:cljr

(compile-when (>= (:major dotnet-version) 5)

(defn- windows? []
  (System.OperatingSystem/IsWindows))

) ;; compile-when
)

#?(
:cljr

(compile-when (< (:major dotnet-version) 5)

(defn- windows? []
  (System.Runtime.InteropServices.RuntimeInformation/IsOSPlatform System.Runtime.InteropServices.OSPlatform/Windows))

) ;; compile-when
)

  
	 
	 
#?(
:clj
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
  (if (windows?)
    (String/Equals (.FullName p1) (.FullName p2) StringComparison/OrdinalIgnoreCase)
	(= (.FullName p1) (.FullName p2))))
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
		  
:cljr  

;; contrary to :clj above, we take either a string or FileSystemInfo.

(defn sub-path?
  [path]
  (if (nil? path)
    false 
    (let [path (if (instance? String path) 
	             (canonicalize path)
				 path)
	      root-path *the-dir*]
      (loop [check-path (cio/dir-info (.FullName ^FileSystemInfo path))]
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