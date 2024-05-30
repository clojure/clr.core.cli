(ns hello-time)

(defn now [] (DateTime/Now))
(defn time-str [t] (.ToString ^DateTime t "F"))
