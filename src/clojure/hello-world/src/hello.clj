(ns hello
  (:require [hello-time :as ht]
            [dct1.test :as t1]))
  
(defn run [opts] 
   (println "This is hello/run talking.")
   (println "opts = " opts)
  (println "Hello world, the time is " (ht/time-str (ht/now))))
  
(defn -main []
  (println "This is hello/-main talking.")
  (println "Hello world, the time is " (ht/time-str (ht/now))))
  
  

(defn test1 [opts]
  (println "this is hello/test1 talking.")
  (println "going to call out to deps-cljr-test1 (github coords)")
  (t1/g 12))  