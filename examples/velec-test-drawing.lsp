;;; Creates a small test drawing for VELEC_BUILD_CABLES (issue #1): one superline "ОПС",
;;; the head device ППК-1 and three detectors on two groups. Load with APPLOAD, run VELEC_TEST_DRAWING,
;;; then VELEC_BUILD_CABLES and select everything.
;;; Device blocks contain a point on layer CONNECTOR_POINT shifted by (1, 0) from the insertion point:
;;; the plugin must route cables to the connector, not to the insertion point.

(setq velec:counter 0)

(defun velec:guid (/ digits)
  (setq velec:counter (1+ velec:counter)
        digits (itoa velec:counter))
  (while (< (strlen digits) 12) (setq digits (strcat "0" digits)))
  (strcat "00000000-0000-4000-8000-" digits)
)

(defun velec:xdata (template pairs / data)
  (setq data (list (cons 1070 2)
                   (cons 1000 "DEVICE_ID") (cons 1000 (velec:guid))
                   (cons 1000 "TEMPLATE") (cons 1000 template)))
  (foreach pair pairs
    (setq data (append data (list (cons 1000 (car pair)) (cons 1000 (cadr pair)))))
  )
  (list -3 (cons "BobrovXDATA" data))
)

(defun velec:route (p1 p2)
  (entmake (list '(0 . "LINE") '(8 . "Трассы") (cons 10 p1) (cons 11 p2)
                 (velec:xdata "SuperLine" '(("NMO_Name" "ОПС")))))
)

(defun velec:device (name pt pairs)
  (entmake (list '(0 . "INSERT") '(2 . "VELEC_TEST_DEVICE") '(8 . "Устройства") (cons 10 pt)
                 (velec:xdata "Device" (cons (list "NMO_Name" name) pairs))))
)

(defun c:VELEC_TEST_DRAWING ()
  (regapp "BobrovXDATA")
  (if (not (tblsearch "BLOCK" "VELEC_TEST_DEVICE"))
    (progn
      (entmake '((0 . "BLOCK") (2 . "VELEC_TEST_DEVICE") (70 . 0) (10 0.0 0.0 0.0)))
      (entmake '((0 . "CIRCLE") (8 . "0") (10 1.0 0.0 0.0) (40 . 1.0)))
      (entmake '((0 . "POINT") (8 . "CONNECTOR_POINT") (10 1.0 0.0 0.0)))
      (entmake '((0 . "ENDBLK")))
    )
  )
  ;; Corridor 0..60 with a branch up at x = 20; connectors are at insertion + (1, 0).
  (velec:route '(0.0 0.0 0.0) '(60.0 0.0 0.0))
  (velec:route '(20.0 0.0 0.0) '(20.0 25.0 0.0))
  (velec:device "ППК-1" '(-1.0 0.0 0.0) '(("conn_1_SuperLine" "ОПС")))
  (velec:device "ИП-1" '(19.0 25.0 0.0) '(("conn_1_SuperLine" "ОПС") ("conn_1_HeadDevice" "ППК-1") ("conn_1_Group" "ШС1")))
  (velec:device "ИП-2" '(59.0 0.0 0.0) '(("conn_1_SuperLine" "ОПС") ("conn_1_HeadDevice" "ППК-1") ("conn_1_Group" "ШС1")))
  (velec:device "ИП-3" '(39.0 0.0 0.0) '(("conn_1_SuperLine" "ОПС") ("conn_1_HeadDevice" "ППК-1") ("conn_1_Group" "ШС2")))
  (command "_.ZOOM" "_E")
  (princ "\nТестовый чертёж создан: выполните VELEC_BUILD_CABLES и выберите все объекты.")
  (princ)
)
