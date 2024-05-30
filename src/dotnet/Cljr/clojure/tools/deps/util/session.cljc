;   Copyright (c) Rich Hickey. All rights reserved.
;   The use and distribution terms for this software are covered by the
;   Eclipse Public License 1.0 (http://opensource.org/licenses/eclipse-1.0.php)
;   which can be found in the file epl-v10.html at the root of this distribution.
;   By using this software in any fashion, you are agreeing to be bound by
;   the terms of this license.
;   You must not remove this notice, or any other, from this software.

(ns ^{:skip-wiki true}
  clojure.tools.deps.util.session
  "Maintains session resources during or across runs of the resolver"
  (:import
    #?(:clj  [java.util.concurrent ConcurrentMap ConcurrentHashMap]
	   :cljr [System.Collections.Concurrent |ConcurrentDictionary`2[System.Object,System.Object]|])
    #?(:clj [java.util.function Function])))


#?(
:clj
(defn make-concurrent-hashmap 
  ^ConcurrentHashMap []
  (ConcurrentHashMap.))

:cljr
(defn make-concurrent-hashmap 
  ^|System.Collections.Concurrent.ConcurrentDictionary`2[System.Object,System.Object]|
  []
  (|System.Collections.Concurrent.ConcurrentDictionary`2[System.Object,System.Object]|.))
)

(def ^|System.Collections.Concurrent.ConcurrentDictionary`2[System.Object,System.Object]| session (make-concurrent-hashmap)) ;; should never be nil

#?(
:clj 
(defn retrieve
  "Read the current value of key from the session. If absent, and if-absent-fn
  is supplied, invoke the fn, set it in the session (if there is one),
  and return the value."
  ([key]
   (.get ^ConcurrentMap session key))
  ([key if-absent-fn]
   (.computeIfAbsent ^ConcurrentMap session key
     (reify Function
       (apply [_f _k]
         (if-absent-fn))))))
		 
:cljr
(defn retrieve
  "Read the current value of key from the session. If absent, and if-absent-fn
  is supplied, invoke the fn, set it in the session (if there is one),
  and return the value."
  ([key]
   (.get_Item session key))
  ([key if-absent-fn]
   (.GetOrAdd session key
		(sys-func [Object Object] [x] (if-absent-fn)))))
)

#?(

:clj 
(defn- get-current-thread-id []
  (.getId (Thread/currentThread)))
  
:cljr
(defn- get-current-thread-id []
  (.ManagedThreadId (System.Threading.Thread/CurrentThread)))

)

(defn retrieve-local
  "Like retrieve, but scoped to a thread-specific key, so never shared across threads."
  ([key]
   (retrieve {:thread (get-current-thread-id) :key key}))
  ([key if-absent-fn]
   (retrieve {:thread (get-current-thread-id) :key key} if-absent-fn)))

(defmacro with-session
  "Create a new empty session and execute the body"
  [& body]
  `(let [prior# session]
     (alter-var-root #'session (constantly (make-concurrent-hashmap)))
     (try
       ~@body
       (finally
         (alter-var-root #'session (constantly prior#))))))