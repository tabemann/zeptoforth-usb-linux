\ Copyright (c) 2023-2024 Travis Bemann
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

compile-to-flash

marker remove-usb-console

begin-module usb-console

  console import
  
  begin-module usb-internal

  task import
  usb-core import
  usb-constants import
  usb-cdc-buffers import

  : start-ep1-ring-transfer-to-host ( -- )
    tx-empty? not if
    tx-used 64 min { tx-bytes }         
    tx-bytes 0 do 
      read-tx EP1-to-Host dpram-address @ i + c! 
    loop

    EP1-to-Host tx-bytes usb-send-data-packet

    then
  ;

  : start-ep1-data-transfer-to-pico ( -- )
    rx-free 63 > if
      EP1-to-Pico 64 usb-receive-data-packet
    then 
  ;
     
  \ USB Start of Frame Interrupts, every 1 ms
  : handle-sof-from-host ( -- )

    EP1-to-Pico busy? @ not if start-ep1-data-transfer-to-pico then
    EP1-to-Host busy? @ not if start-ep1-ring-transfer-to-host then 
  ;

    \ Byte available to read from rx ring buffer ?
  : usb-key? ( -- key? )   
    rx-empty? not 
  ;

    \ Client connected and tx ring buffer capacity available ?
  : usb-emit? ( -- emit? )
    usb-dtr? tx-full? not and
  ;

  : usb-emit ( c -- )
    tx-full? if
      begin pause-reschedule-last tx-full? not until
    then
    write-tx
  ;

  : usb-key ( -- c)
    begin
      rx-empty? if
        pause-reschedule-last false
      else
        read-rx true    
      then
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

  : usb-wait-for-device-configured ( -- )

    cr ." Waiting for Host to set USB Device Configuration ... " cr

    begin pause-reschedule-last usb-device-configured? @ not until
    
    cr

    ." --------------------------------------------------------- " cr
    ." USB Device Configured, Starting SOF callback handler now " cr

    ['] handle-sof-from-host sof-callback-handler !
  ;

  : usb-wait-for-client-connect ( -- )

    cr ." Waiting for Client to Connect (DTR signal from Host) ... " cr

    begin pause-reschedule-last usb-dtr? until
 
    cr

    ." --------------------------------------------------------- " cr
    ." Client Connected, starting USB CDC/ACM Serial Console now"  cr

  ;

  \ Initialize USB console
  : init-usb-console ( -- )

    init-usb
    init-tx-ring
    init-rx-ring

    usb-insert-device    
    usb-wait-for-device-configured
    usb-wait-for-client-connect
    usb-set-modem-online
    switch-to-usb-console
    welcome-usb
  ;

  initializer init-usb-console
    
  end-module> import

  \ Select the USB console
  : usb-console ( -- ) switch-to-usb-console ;

end-module

compile-to-ram

\ USB_4_CDC_CONSOLE V4 - LINUX COMPATIBILITY END =============================================== 
