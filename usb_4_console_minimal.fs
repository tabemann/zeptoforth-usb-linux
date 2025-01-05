\ Copyright (c) 2023-2025 Travis Bemann
\ Copyright (c) 2025 Serialcomms (GitHub)
\
\ Permission is hereby granted, free of charge, to any person obtaining a copy
\ of this software and associated documentation files (the "Software"), to deal
\ in the Software without restriction, including without limitation the rights
\ to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
\ copies of the Software, and to permit persons to whom the Software is
\ furnished to do so, subject to the following conditions:
\
\ The above copyright notice and this permission notice shall be included in
\ all copies or substantial portions of the Software.
\
\ THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
\ IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
\ FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
\ AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
\ LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
\ OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
\ SOFTWARE.

\ IMPORTANT - many terminal clients use the same control keys as zeptoforth.
\ use false usb::usb-special-enabled ! to allow use of more terminal clients.
\ Note that some clients may hang if Pico reboots and does not recover.

compile-to-flash

continue-module usb

  console import

  begin-module usb-console-internal

    task import
    usb-core import
    usb-constants import
    usb-cdc-buffers import
    core-lock import

    \ Transmit core lock
    core-lock-size buffer: tx-core-lock

    \ Receive core lock
    core-lock-size buffer: rx-core-lock

    : start-ep1-data-transfer-to-host ( -- )
      tx-empty? not if
        ep1-start-ring-transfer-to-host
      then
    ;

    : start-ep1-data-transfer-to-pico ( -- )
      rx-free 63 > if
        EP1-to-Pico 64 usb-receive-data-packet
      then
    ;

    \ USB Start of Frame Interrupts, every 1 ms
    : handle-sof-from-host ( -- )
      EP1-to-Pico endpoint-busy? @ not if start-ep1-data-transfer-to-pico then
      EP1-to-Host endpoint-busy? @ not if start-ep1-data-transfer-to-host then
    ;

    \ Byte available to read from rx ring buffer ?
    : usb-key? ( -- key? )
      rx-empty? not
    ;

    \ Client connected and tx ring buffer capacity to host available ?
    : usb-emit? ( -- emit? )
      usb-device-configured? @ usb-dtr? and tx-full? not and
    ;

    : usb-emit ( c -- )
      begin
        [:
          [:
            usb-emit? dup if usb-try-set-modem-online swap write-tx then
          ;] critical
        ;] tx-core-lock with-core-lock
        dup not if pause-reschedule-last then
      until
    ;

    : usb-key ( -- c)
      begin
        [:
          [:
            usb-key? dup if usb-try-set-modem-online read-rx swap then
          ;] critical
        ;] rx-core-lock with-core-lock
        dup not if pause-reschedule-last then
      until
    ;

    : usb-flush-console ( -- )
      begin pause-reschedule-last tx-empty? until
    ;

    \ Switch to USB console
    : switch-to-usb-console

      ['] usb-key? key?-hook !
      ['] usb-key key-hook !
      ['] usb-emit? emit?-hook !
      ['] usb-emit emit-hook !

      ['] usb-emit? error-emit?-hook !
      ['] usb-emit error-emit-hook !
      ['] usb-flush-console flush-console-hook !
      ['] usb-flush-console error-flush-console-hook !
    ;

    \ Initialize USB console
    : init-usb-console ( -- )

      tx-core-lock init-core-lock
      rx-core-lock init-core-lock
      
      init-usb
      init-tx-ring
      init-rx-ring

      usb-insert-device
      ['] handle-sof-from-host sof-callback-handler !
      switch-to-usb-console
    ;

    initializer init-usb-console

  end-module> import

  \ Select the USB serial console
  : usb-console ( -- ) switch-to-usb-console ;

  \ Set the curent input to usb within an xt
  : with-usb-input ( xt -- )
    ['] usb-key ['] usb-key? rot with-input
  ;
  
  \ Set the current output to usb within an xt
  : with-usb-output ( xt -- )
    ['] usb-emit ['] usb-emit? rot ['] usb-flush-console swap with-output
  ;
  
  \ Set the current error output to usb within an xt
  : with-usb-error-output ( xt -- )
    ['] usb-emit ['] usb-emit? rot ['] usb-flush-console swap with-error-output
  ;
  
end-module

\ : turnkey cr ." USB Serial Console Turnkey Test " cr ; \ not working here - ask Travis

compile-to-ram

\ USB_4_CDC_CONSOLE (JANUARY 2025) END ===============================================
