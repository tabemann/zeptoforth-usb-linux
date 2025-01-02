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

marker remove-usb-cdc-buffers

  begin-module usb-cdc-buffers

  \ Constant for number of bytes to buffer
  4096 constant tx-buffer-size

  \ Constant for number of bytes to buffer
  4096 constant rx-buffer-size

  \ RAM variable for rx buffer read-index
  variable tx-read-index
  
  \ RAM variable for rx buffer read-index
  variable rx-read-index

  \ RAM variable for rx buffer write-index
  variable tx-write-index
  
  \ RAM variable for rx buffer write-index
  variable rx-write-index
  
  \ tx buffer size mask
  tx-buffer-size 1 - constant tx-buffer-mask 
 
  \ rx buffer size mask
  rx-buffer-size 1 - constant rx-buffer-mask
    
  \ tx buffer to Host
  tx-buffer-size buffer: tx-buffer

  \ rx buffer to Pico
  rx-buffer-size buffer: rx-buffer
    
  : init-tx-ring ( -- )
    0 tx-read-index !
    0 tx-write-index !
    tx-buffer tx-buffer-size 0 fill
  ;

  : init-rx-ring ( -- )
    0 rx-read-index !
    0 rx-write-index ! 
    rx-buffer rx-buffer-size 0 fill
  ;

  : tx-size ( -- u )
    tx-buffer-size 1 - 
  ;

  : rx-size ( -- u )
    rx-buffer-size 1 -
  ;

  : tx-used ( -- u )
    tx-read-index @ { read-index }
    tx-write-index @ { write-index }
    read-index write-index <= if
      write-index read-index -
    else
      tx-buffer-size read-index - write-index +
    then
  ;

  : rx-used ( -- u )   
    rx-read-index @ { read-index }
    rx-write-index @ { write-index }
    read-index write-index <= if
      write-index read-index -
    else
      rx-buffer-size read-index - write-index +
    then  
  ;
    
  \ Get number of free bytes available in tx buffer
  : tx-free ( -- bytes ) 
    tx-buffer-size 1 - tx-used - 
  ;

  \ Get number of free bytes available in rx buffer
  : rx-free ( -- bytes )   
    rx-buffer-size 1 - rx-used -  
  ;

  \ Get whether the tx buffer is full
  : tx-full? ( -- f )
    tx-write-index @ 1 + tx-buffer-mask and tx-read-index @ =
  ;
  
  \ Get whether the rx buffer is full
  : rx-full? ( -- f )    
    rx-write-index @ 1 + rx-buffer-mask and rx-read-index @ =
  ;

   \ Get whether the tx buffer is empty
  : tx-empty? ( -- f )
    tx-write-index @ tx-read-index @ = 
  ;

  \ Get whether the rx buffer is empty
  : rx-empty? ( -- f )  
    rx-write-index @ rx-read-index @ = 
  ;

  \ USB read byte from tx ring buffer 
  : read-tx ( -- c )  
    tx-empty? if 0 else
    tx-read-index @ tx-buffer + c@     
    tx-read-index @ 1 + tx-buffer-mask and tx-read-index !  
    then
  ;

  \ Console read byte from rx ring buffer
  : read-rx ( -- c )
    rx-empty? if 0 else
    rx-read-index @ rx-buffer + c@
    rx-read-index @ 1 + rx-buffer-mask and rx-read-index !
    then
  ;

  \ Console write byte to tx ring buffer
  : write-tx ( c -- ) 
    tx-full? if drop else
      tx-write-index @ tx-buffer + c!
      tx-write-index @ 1 + tx-buffer-mask and tx-write-index !
    then
  ;

  \ USB write byte to rx ring buffer
  : write-rx ( c -- )
    rx-full? if drop else
      rx-write-index @ rx-buffer + c!
      rx-write-index @ 1 + rx-buffer-mask and rx-write-index !
    then
  ;

  end-module

  compile-to-ram

  \ USB_2_CDC_BUFFERS (NEW) END =============================================== 