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

begin-module usb-core

armv6m import
task import
serial import
interrupt import

usb-constants import
usb-cdc-buffers import

\ Device configuration set
variable usb-device-configured?

\ USB Start-Of-Frame callback handler
variable sof-callback-handler

\ USB control-C, control-T
variable usb-control-keys-enabled?

\ Control key pressed
variable control-key

\ (Host - DTR) Data Terminal Ready
variable DTR?

\ (Host - RTS) Ready to Send
variable RTS?

\ (Pico - DSR) Data Set Ready
variable DSR?

\ (Pico - DCD) Data Carrier Detect
variable DCD?

\ (Pico - RI) Ring Indicate
variable RING?

: usb-core-debug ( xt -- )
  emit-hook @ { saved-emit-hook }
  emit?-hook @ { saved-emit?-hook }
  flush-console-hook @ { saved-flush-console-hook }
  pause-hook @ { saved-pause-hook }
  ['] internal::serial-emit emit-hook !
  ['] internal::serial-emit? emit?-hook !
  [: ;] flush-console-hook !
  [: ;] pause-hook !
  try
  saved-emit-hook emit-hook !
  saved-emit?-hook emit?-hook !
  saved-flush-console-hook flush-console-hook !
  saved-pause-hook pause-hook !
  ?raise
;

: usb-buffer-offset ( addr -- addr' ) USB_DPRAM_Base - ;

: reset-usb-hardware ( -- )
  RESETS_USBCTRL RESETS_RESET_Set !
  RESETS_USBCTRL RESETS_RESET_Clr !
  begin RESETS_RESET_DONE @ not RESETS_USBCTRL and while repeat
;

: ENDPOINT_CONTROL_TO_HOST ( endpoint -- address )     \ USB endpoint control register address (in)
  3 lshift USB_DPRAM_Base +
;

: ENDPOINT_CONTROL_TO_PICO ( endpoint -- address )     \ USB endpoint control register address (out)
  3 lshift [ USB_DPRAM_Base cell+ ] literal +
;

: BUFFER_CONTROL_TO_HOST ( endpoint -- address )       \ USB buffer control register address (in)
  3 lshift [ USB_DPRAM_Base $80 + ] literal +
;

: BUFFER_CONTROL_TO_PICO ( endpoint -- address )       \ USB buffer control register address (out)
  3 lshift [ USB_DPRAM_Base $80 + cell+ ] literal +
;

create device-data

$12 c, $01 c,                                                       \ USB_DEVICE_DESCRIPTOR
$00 c, $02 c,                                                       \ USB_VERSION_BCD
$EF c, $02 c, $01 c,                                                \ USB_MISC_IAD_DEVICE (IAD = Interface Association Device)
$40 c,                                                              \ USB_EP0_MAX
$83 c, $04 c,                                                       \ USB_VENDOR_ID
$40 c, $57 c,                                                       \ USB_PRODUCT_ID
$00 c, $02 c,                                                       \ USB_PRODUCT_BCD
$00 c,                                                              \ STRING_MANUFACTURER (0 = none)
$00 c,                                                              \ STRING_PRODUCT      (0 = none)
$00 c,                                                              \ STRING_SERIAL       (0 = none)
$01 c,                                                              \ CONFIGURATIONS

here device-data - cell align, constant device-data-size

create config-data

$09 c, $02 c, $4B c, $00 c, $02 c, $01 c, $00 c, $80 c, $FA c,      \ Configuration Descriptor (75 bytes)
$08 c, $0B c, $00 c, $02 c, $02 c, $02 c, $00 c, $00 c,             \ Interface Association Descriptor (IAD)
$09 c, $04 c, $00 c, $00 c, $01 c, $02 c, $02 c, $00 c, $00 c,      \ CDC Class Interface Descriptor (CCI)
$05 c, $24 c, $00 c, $20 c, $01 c,                                  \ CDC Functional Descriptor - Header (CDC Class revision 1.20)
$04 c, $24 c, $02 c, $06 c,                                         \ CDC Functional Descriptor - Abstract Control Management
$05 c, $24 c, $06 c, $00 c, $01 c,                                  \ CDC Functional Descriptor - Union
$05 c, $24 c, $01 c, $01 c, $00 c,                                  \ CDC Functional Descriptor - Call Management
$07 c, $05 c, $83 c, $03 c, $10 c, $00 c, $01 c,                    \ Endpoint Descriptor: EP3 In - (max packet size = 16)

$09 c, $04 c, $01 c, $00 c, $02 c, $0A c, $00 c, $00 c, $00 c,      \ CDC Data Class Interface Descriptor: CDC Data
$07 c, $05 c, $81 c, $02 c, $40 c, $00 c, $00 c,                    \ Endpoint Descriptor: EP1 In  - Bulk Transfer Type
$07 c, $05 c, $01 c, $02 c, $40 c, $00 c, $00 c,                    \ Endpoint Descriptor: EP1 Out - Bulk Transfer Type

here config-data - cell align, constant config-data-size

create string-data

$04 c, $03 c, $09 c, $04 c,                                         \ Language String Descriptor LANGUAGE_ENGLISH_US

here string-data - cell align, constant string-data-size

begin-structure line-state-notification-descriptor

cfield: line-request-type
cfield: line-request
hfield: line-value
hfield: line-index
hfield: line-length
hfield: dce-signals

end-structure  \ 10 bytes to send

begin-structure usb-endpoint-profile

field: endpoint-tx?         \ Is endpoint transmit to host (USB Direction = In)
field: endpoint-ack?        \ Send ACK on buffer completion (control transfers)
field: endpoint-busy?       \ Is endpoint currently active tx/rx ? (As set by Forth - not USB hardware)
field: endpoint-number      \ Endpoint Number, for debug etc.
field: max-packet-size      \ Endpoint maximum packet size
field: dpram-address        \ DPRAM address in Pico hardware
field: next-pid             \ Endpoint next PID (0 for PID0 or 8192 for PID1)
field: buffer-control       \ Endpoint buffer control register address
field: endpoint-control     \ Endpoint control register address
field: transfer-type        \ Endpoint transfer type (Control, Bulk, Interrupt)
field: transfer-bytes       \ Endpoint transfer bytes (sent or received on buffer completion)
field: processed-bytes      \ Endpoint processed bytes (Multipacket)
field: pending-bytes        \ Endpoint bytes pending (Multipacket)
field: total-bytes          \ Total transfer bytes (Multipacket)
field: source-address       \ Source data address (Multipacket transmit)
field: callback-handler     \ Callback handler for CDC set line coding

end-structure

begin-structure usb-setup-command

cfield: setup-descriptor-type
cfield: setup-descriptor-index
cfield: setup-request-type        \ Setup packet request type
cfield: setup-direction?          \ Setup direction ( &80 = to Host)
cfield: setup-recipient           \ Setup Recipient ( device, interface, endpoint )
cfield: setup-request             \ Setup packet request
hfield: setup-length              \ Setup packet length
hfield: setup-value               \ Setup packet value
hfield: setup-index               \ Setup packet index

end-structure

begin-structure cdc-line-coding-profile

field: cdc-line-baud
cfield: cdc-line-parity
cfield: cdc-line-stop
cfield: cdc-line-data

end-structure   \ 7 bytes to send

usb-setup-command buffer: usb-setup

cdc-line-coding-profile buffer: cdc-line-coding

line-state-notification-descriptor buffer: line-state-notification

usb-endpoint-profile buffer: EP0-to-Host    \ Default endpoint 0 to Host
usb-endpoint-profile buffer: EP0-to-Pico    \ Default endpoint 0 to Pico
usb-endpoint-profile buffer: EP1-to-Host    \ Function endpoint 1 to Host
usb-endpoint-profile buffer: EP1-to-Pico    \ Function endpoint 1 to Pico
usb-endpoint-profile buffer: EP3-to-Host    \ Function endpoint 3 to Host

: init-port-signals ( -- )

  false DTR? !   \ Data Terminal Ready - from Host to Pico
  false RTS? !   \ Ready to Send       - from Host to Pico
  false DSR? !   \ Data Set Ready      - from Pico to Host
  false DCD? !   \ Data Carrier Detect - from Pico to Host
  false RING? !  \ Ring Indicate       - from Pico to Host
;

: init-cdc-line-coding ( -- )
  115200 cdc-line-coding cdc-line-baud !     \ 115200 baud
       0 cdc-line-coding cdc-line-parity c!  \ no parity
       0 cdc-line-coding cdc-line-stop c!    \ 1 stop bit
       8 cdc-line-coding cdc-line-data c!    \ 8 data bits
;

: init-line-state-notification ( -- )
  $A1 line-state-notification line-request-type c!
  $20 line-state-notification line-request c!
    0 line-state-notification line-value h!
    0 line-state-notification line-index h!
    2 line-state-notification line-length h!
    0 line-state-notification dce-signals h!
;

: usb-init-setup-packet ( -- )
  usb-setup usb-setup-command 0 fill
;

: init-ep-common { ep-max ep-tx? ep-number endpoint }
  endpoint usb-endpoint-profile 0 fill
  ep-tx? endpoint endpoint-tx? !
  ep-number endpoint endpoint-number !
  ep-max endpoint max-packet-size !
  false endpoint endpoint-busy? !
  false endpoint endpoint-ack? !
  ep-tx? if
   ep-number BUFFER_CONTROL_TO_HOST else
   ep-number BUFFER_CONTROL_TO_PICO
  then endpoint buffer-control !
  0 endpoint buffer-control @ !
;

: init-usb-endpoint-0 { ep-max ep-tx? endpoint -- }
  ep-max ep-tx? 0     endpoint init-ep-common
  EP0_DPRAM_SHARED    endpoint dpram-address !
  USB_EP_TYPE_CONTROL endpoint transfer-type !
  \ there is no endpoint control register for EP0, interrupt enable for EP0 comes from SIE_CTRL
;

: init-ep-x-to-host { ep-type ep-number endpoint -- }
  128 ep-number 1 - * { dpram-offset }
  $0180 dpram-offset + { ep-dpram-address } \ first half of usb hardware dpram
  ep-type EP_CTRL_BUFFER_TYPE_LSB lshift { ep-control }
  USB_EP_ENABLE_INTERRUPT_PER_BUFFER ep-control or to ep-control
  USB_EP_ENABLE ep-control or to ep-control
  ep-dpram-address ep-control or to ep-control
  ep-control ep-number ENDPOINT_CONTROL_TO_HOST !
  ep-dpram-address $5010_0000 + endpoint dpram-address !
;

: init-ep-x-to-pico { ep-type ep-number endpoint -- }
  128 ep-number 1 - * { dpram-offset }
  $0900 dpram-offset + { ep-dpram-address } \ second half of usb hardware dpram
  ep-type EP_CTRL_BUFFER_TYPE_LSB lshift { ep-control }
  USB_EP_ENABLE_INTERRUPT_PER_BUFFER ep-control or to ep-control
  USB_EP_ENABLE ep-control or to ep-control
  ep-dpram-address ep-control or to ep-control
  ep-control ep-number ENDPOINT_CONTROL_TO_PICO !
  ep-dpram-address $5010_0000 + endpoint dpram-address !
;

: init-usb-endpoint-x { ep-max ep-tx? ep-type ep-number endpoint -- }
  ep-type endpoint transfer-type !
  ep-max ep-tx? ep-number endpoint init-ep-common
  ep-type ep-number endpoint
  ep-tx? if init-ep-x-to-host else init-ep-x-to-pico then
;

\ Initialize USB default endpoints 0
: init-usb-default-endpoints ( -- )
  64 true  EP0-to-Host init-usb-endpoint-0
  64 false EP0-to-Pico init-usb-endpoint-0
;

\ Initialize CDC/ACM Function Endpoints
: init-usb-function-endpoints ( -- )
  64 true  USB_EP_TYPE_BULK       1 EP1-to-Host init-usb-endpoint-x
  64 false USB_EP_TYPE_BULK       1 EP1-to-Pico init-usb-endpoint-x
  16 true  USB_EP_TYPE_INTERRUPT  3 EP3-to-Host init-usb-endpoint-x
;

: usb-toggle-data-pid { endpoint -- }
  endpoint next-pid @ if
   USB_BUF_CTRL_DATA0_PID
  else
   USB_BUF_CTRL_DATA1_PID
  then endpoint next-pid !
;

: usb-set-buffer-available { endpoint -- }
  code[ b> >mark b> >mark b> >mark b> >mark b> >mark b> >mark ]code
  USB_BUF_CTRL_AVAIL endpoint buffer-control @ bis!
;

: usb-dispatch-buffer { endpoint -- }
  USB_BUF_CTRL_FULL endpoint buffer-control @ bis!
  endpoint usb-set-buffer-available
;

: usb-receive-zero-length-packet  { endpoint -- }
  endpoint next-pid @ endpoint buffer-control @ !
  endpoint usb-set-buffer-available
;

: usb-send-zero-length-packet { endpoint -- }
  endpoint next-pid @ USB_BUF_CTRL_FULL or endpoint buffer-control @ !
  endpoint usb-set-buffer-available
;

: usb-send-stall-packet { endpoint -- }
  true endpoint endpoint-busy? !
  endpoint next-pid @ endpoint buffer-control @ !
  code[ b> >mark b> >mark b> >mark b> >mark b> >mark b> >mark ]code
  USB_BUF_CTRL_STALL endpoint buffer-control @ bis!
;

: usb-send-data-packet { endpoint bytes -- }
  true endpoint endpoint-busy? !
  0 endpoint transfer-bytes !
  endpoint next-pid @ bytes or endpoint buffer-control @ !
  endpoint usb-dispatch-buffer
;

: usb-receive-data-packet { endpoint bytes -- }
  true endpoint endpoint-busy? !
  0 endpoint transfer-bytes !
  endpoint next-pid @ bytes or endpoint buffer-control @ !
  USB_BUF_CTRL_AVAIL endpoint buffer-control @ bis!
;

\ Acknowledge USB control-out request (zero length packet)
: usb-ack-control-out-request ( -- )
  EP0-to-Host usb-send-zero-length-packet
;

\ Acknowledge USB control-in request (zero length packet)
: usb-ack-control-in-request ( -- )
  EP0-to-Pico usb-receive-zero-length-packet
;

: usb-get-device-address ( -- )
  USB_DEVICE_ADDRESS @ $1F and
;

\ Set USB hardware device address
: usb-set-device-address ( -- )
  usb-ack-control-out-request
  1000. timer::delay-us
  usb-setup setup-value h@ USB_DEVICE_ADDRESS !
;

: usb-build-data-packet { endpoint bytes source-data -- }
  \ do not exceed max packet size
  endpoint max-packet-size @ bytes min { packet-bytes }
  source-data endpoint dpram-address @ packet-bytes move
;

: usb-start-transfer-to-host { host-endpoint total-data-bytes source-data-address -- }
  total-data-bytes host-endpoint total-bytes !
  source-data-address host-endpoint source-address !
  \ do not exceed max packet size
  host-endpoint max-packet-size @ total-data-bytes min { packet-bytes }
  host-endpoint packet-bytes source-data-address usb-build-data-packet
  host-endpoint packet-bytes usb-send-data-packet
;

: usb-start-control-transfer-to-host { total-data-bytes source-data-address -- }
  0 EP0-to-Host transfer-bytes !
  0 EP0-to-Host processed-bytes !
  total-data-bytes EP0-to-Host total-bytes !
  total-data-bytes EP0-to-Host pending-bytes !

  usb-get-device-address if
   true EP0-to-Host endpoint-ack? !
   EP0-to-Host total-data-bytes source-data-address usb-start-transfer-to-host
  else
   false EP0-to-Host endpoint-ack? !
   EP0-to-Host total-data-bytes source-data-address usb-start-transfer-to-host
   usb-ack-control-in-request
  then
;

: usb-end-control-transfer-to-host ( -- )
  usb-ack-control-in-request
  false EP0-to-Host endpoint-ack? !
  false EP0-to-Host endpoint-busy? !
;

: usb-send-device-descriptor ( -- )
  usb-setup setup-length h@ device-data-size = if
   device-data-size
  else
   8
  then
  device-data usb-start-control-transfer-to-host
;

: usb-send-config-descriptor ( -- )
  usb-setup setup-length h@ config-data-size = if
  config-data-size
  else
   9
  then
  config-data usb-start-control-transfer-to-host
;

: usb-send-string-descriptor ( -- )
  usb-setup setup-index h@ 0 = if
   4 string-data usb-start-control-transfer-to-host
  else
   usb-ack-control-in-request
  then
;

: usb-stall-ep0-request-to-pico ( -- )
  EP0_STALL_TO_PICO EP0_STALL_ARM bis!
  EP0-to-Host usb-send-stall-packet
;

: usb-stall-ep0-respond-to-host ( -- )
  EP0_STALL_TO_HOST EP0_STALL_ARM bis!
  EP0-to-Host usb-send-stall-packet
;

\ inform host of Pico's line signal state
: usb-send-line-state-notification ( -- )
  begin pause-reschedule-last EP3-to-Host endpoint-busy? @ not until
  0 line-state-notification dce-signals !
  DSR?  @ if BITMAP_DSR line-state-notification dce-signals hbis! then
  DCD?  @ if BITMAP_DCD line-state-notification dce-signals hbis! then
  RING? @ if BITMAP_RING line-state-notification dce-signals hbis! then
  EP3-to-Host 10 line-state-notification usb-start-transfer-to-host
;

\ tell host client that Pico is online (useful to some clients)
: usb-set-modem-online ( -- )
  true DCD? !   \ data carrier detected
  true DSR? !   \ data set (modem) ready
  false RING? ! \ Ring Indicate off
  usb-send-line-state-notification
;

\ tell host client that Pico is offline (useful to some clients)
: usb-set-modem-offline ( -- )
  false DCD? !  \ data carrier lost
  false DSR? !  \ data set (modem) not ready
  false RING? ! \ Ring Indicate off
  usb-send-line-state-notification
;

: usb-set-device-configuration ( -- )
  init-usb-function-endpoints
  init-line-state-notification
  init-cdc-line-coding
  USB_BUF_CTRL_DATA1_PID EP0-to-Host next-pid !
  \ Enable Start of Frame interrupts for EP1 tx/rx
  USB_INTS_START_OF_FRAME USB_INTE bis!
  EP1-to-Pico 64 usb-receive-data-packet
  usb-ack-control-out-request
  true usb-device-configured? !
;

: usb-send-descriptor-to-host ( -- )
  usb-setup setup-descriptor-type c@ case
   USB_DT_DEVICE of usb-send-device-descriptor endof
   USB_DT_CONFIG of usb-send-config-descriptor endof
   USB_DT_STRING of usb-send-string-descriptor endof
   \ for Linux hosts if device used on high-speed port
   USB_DT_QUALIFIER of usb-send-device-descriptor endof
   usb-stall-ep0-respond-to-host
  endcase
;

: usb-setup-type-standard-respond-to-host ( -- )
  usb-setup setup-request c@ case
   USB_REQUEST_GET_DESCRIPTOR of usb-send-descriptor-to-host endof
   usb-stall-ep0-respond-to-host
  endcase
;

: usb-setup-type-standard-request-to-pico ( -- )
  usb-setup setup-request c@ case
   USB_REQUEST_SET_ADDRESS of usb-set-device-address endof
   USB_REQUEST_SET_CONFIGURATION of usb-set-device-configuration endof
   usb-stall-ep0-request-to-pico
  endcase
;

: usb-setup-type-standard ( -- )
  usb-setup setup-direction? c@ if
   usb-setup-type-standard-respond-to-host
  else
   usb-setup-type-standard-request-to-pico
  then
;

\ tell host our speed/data/parity/stop bit settings - e.g. 115200-8-N-1
: usb-class-get-line-coding ( -- )
  7 cdc-line-coding usb-start-control-transfer-to-host
;

\ start callback if host wants us to set our speed/data/parity/stop bit settings
: usb-class-set-line-coding ( -- )
  true EP0-to-Pico callback-handler !
  EP0-to-Pico 7 usb-receive-data-packet
;

\ DTE signals from host terminal client (zeptocomjs, Minicom, PuTTY, tio et al)
: usb-class-set-line-control ( -- )
  usb-ack-control-out-request
  usb-setup setup-value h@ BITMAP_DTR and if true DTR? ! else false DTR? ! then
  usb-setup setup-value h@ BITMAP_RTS and if true RTS? ! else false RTS? ! then
  DTR? @ if
  \ switch over to cdc console here ?
  else
  \ switch back to uart console here ?
  then
;

: usb-class-set-line-break ( -- )

  \ some clients may toggle break by sending with value = $FFFF followed by value = $0000
  \ CDC break is a usable signal state here as many clients are capable of sending it.

  usb-setup setup-value h@ $FFFF = if

  [:

  cr
  ." --------------------------------------------------------- " cr

  ." TX Busy  is " EP1-to-Host endpoint-busy? @ h.8 cr
  ." TX Full  is " tx-full? h.8 cr
  ." TX Empty is " tx-empty? h.8 cr
  ." TX Free  is " tx-free h.8 cr
  ." TX Used  is " tx-used h.8 cr
  ." TX Read  is " usb-cdc-buffers::tx-read-index @ h.8 cr
  ." TX Write is " usb-cdc-buffers::tx-write-index @ h.8 cr

  cr

  ." RX Busy  is " EP1-to-Pico endpoint-busy? @ h.8 cr
  ." RX Full  is " rx-full? h.8 cr
  ." RX Empty is " rx-empty? h.8 cr
  ." RX Free  is " rx-free h.8 cr
  ." RX Used  is " rx-used h.8 cr
  ." RX Read  is " usb-cdc-buffers::rx-read-index @ h.8 cr
  ." RX Write is " usb-cdc-buffers::rx-write-index @ h.8 cr

  ;] usb-core-debug

  then

  usb-ack-control-out-request
;

: usb-setup-type-class-respond-to-host ( -- )
  usb-setup setup-request c@ case
   CDC_CLASS_GET_LINE_CODING of usb-class-get-line-coding endof
  endcase

  [:

  cr
  ." --------------------------------------------------------- " cr
  ." USB Setup Type Class - Respond to Host" cr
  ." Direction is " usb-setup setup-direction? c@ h.8 cr
  ." Request   is " usb-setup setup-request c@ h.8 cr
  ." Value     is " usb-setup setup-value h@ h.8 cr

  ;] usb-core-debug

;

: usb-setup-type-class-request-to-pico ( -- )
  usb-setup setup-request c@ case
   CDC_CLASS_SET_LINE_BREAK of usb-class-set-line-break endof
   CDC_CLASS_SET_LINE_CODING of usb-class-set-line-coding endof
   CDC_CLASS_SET_LINE_CONTROL of usb-class-set-line-control endof
  endcase

  [:

  cr
  ." --------------------------------------------------------- " cr
  ." USB Setup Type Class - Request to Pico" cr
  ." Direction is " usb-setup setup-direction? c@ h.8 cr
  ." Request   is " usb-setup setup-request c@ h.8 cr
  ." Value     is " usb-setup setup-value h@ h.8 cr

  ;] usb-core-debug

;

: usb-setup-type-class ( -- )
  usb-setup setup-direction? c@ if
   usb-setup-type-class-respond-to-host
  else
   usb-setup-type-class-request-to-pico
  then
;

: usb-prepare-setup-packet ( -- )
  USB_SETUP_PACKET 0 + c@ $80 and usb-setup setup-direction? c!
  USB_SETUP_PACKET 0 + c@ $1f and usb-setup setup-recipient c!
  USB_SETUP_PACKET 0 + c@ $60 and usb-setup setup-request-type c!

  USB_SETUP_PACKET 1 + c@ usb-setup setup-request c!
  USB_SETUP_PACKET 2 + c@ usb-setup setup-descriptor-index c!
  USB_SETUP_PACKET 3 + c@ usb-setup setup-descriptor-type c!

  USB_SETUP_PACKET 2 + h@ usb-setup setup-value h!
  USB_SETUP_PACKET 4 + h@ usb-setup setup-index h!
  USB_SETUP_PACKET 6 + h@ usb-setup setup-length h!
;

: usb-prepare-setup-direction ( -- )
  usb-setup setup-direction? c@ if
   USB_BUF_CTRL_AVAIL  EP0-to-Host buffer-control @ bis!
   USB_BUF_CTRL_AVAIL  EP0-to-Pico buffer-control @ bic!
  else
  USB_BUF_CTRL_AVAIL  EP0-to-Host buffer-control @ bic!
  USB_BUF_CTRL_AVAIL  EP0-to-Pico buffer-control @ bis!
 then
;

: usb-handle-setup-packet ( -- )
  USB_SIE_STATUS_SETUP_REC USB_SIE_STATUS !
  usb-prepare-setup-packet
  usb-prepare-setup-direction
  USB_BUF_CTRL_DATA1_PID EP0-to-Host next-pid !
  USB_BUF_CTRL_DATA1_PID EP0-to-Pico next-pid !
  usb-setup setup-request-type c@ case
   USB_REQUEST_TYPE_STANDARD of usb-setup-type-standard endof
   USB_REQUEST_TYPE_CLASS of usb-setup-type-class endof
   usb-ack-control-out-request
  endcase
;

: usb-handle-bus-reset ( -- )
  USB_SIE_STATUS_BUS_RESET USB_SIE_STATUS !
  0 USB_DEVICE_ADDRESS !
  false usb-device-configured? !
;

: usb-update-transfer-bytes { endpoint -- }
  endpoint buffer-control @ @ USB_BUF_CTRL_LENGTH_MASK and endpoint transfer-bytes !
;

: usb-update-endpoint-byte-counts { endpoint -- }
  endpoint processed-bytes @ endpoint transfer-bytes @ + endpoint processed-bytes !
  endpoint total-bytes @ endpoint processed-bytes @ - endpoint pending-bytes !
;

: usb-get-continue-source-address { endpoint -- }
  endpoint source-address @ endpoint processed-bytes @ +
;

: usb-get-next-packet-size-to-host { endpoint -- }
  endpoint pending-bytes @ endpoint max-packet-size @ min 0 max  \ check against negative values
;

: usb-handle-start-of-frame ( -- )
  \ this could probably be designed out eventually but
  \ is useful as a kind of console watchdog for now
  \ Read SOF register to clear IRQ
  USB_SOF_READ @ { frame-number }
  sof-callback-handler @ ?execute
;

: ep0-handler-to-host ( -- )
  \ Write to clear
  USB_BUFFER_STATUS_EP0_TO_HOST USB_BUFFER_STATUS bis!
  EP0-to-Host usb-update-transfer-bytes
  EP0-to-Host usb-toggle-data-pid
  EP0-to-Host usb-update-endpoint-byte-counts
  EP0-to-Host usb-get-next-packet-size-to-host { next-packet-size }
  next-packet-size if
   EP0-to-Host usb-get-continue-source-address { continue-source-address }
   EP0-to-Host next-packet-size continue-source-address usb-build-data-packet
   EP0-to-Host next-packet-size usb-send-data-packet
  else
   EP0-to-Host endpoint-ack? @ if usb-end-control-transfer-to-host then
  then
;

\ Update our line speed/data/parity/stop bit settings
\ e.g. 115200-8-N-1 as requested by host client
: ep0-handler-to-pico-callback ( -- )
  EP0-to-Pico dpram-address @ cdc-line-coding 7 move
  false EP0-to-Pico callback-handler !
  usb-ack-control-out-request
;

: ep0-handler-to-pico ( -- )
  USB_BUFFER_STATUS_EP0_TO_PICO USB_BUFFER_STATUS bis!
  EP0-to-Pico usb-update-transfer-bytes
  EP0-to-Pico usb-toggle-data-pid
  EP0-to-Pico callback-handler @ if
  ep0-handler-to-pico-callback then
;

\ de-queue bytes from ring buffer to EP1 dpram
: ep1-start-ring-transfer-to-host ( -- )
  tx-used 64 min { tx-bytes }
  tx-bytes 0 do
   read-tx EP1-to-Host dpram-address @ i + c!
  loop
  EP1-to-Host tx-bytes usb-send-data-packet
;

: ep1-handler-to-host ( -- )
  EP1-to-Host usb-update-transfer-bytes
  EP1-to-Host usb-toggle-data-pid
  tx-empty? if
   \ USB 2.0, page 53, section 5.8.3 - bulk transfer complete (required by some, but not all clients)
   EP1-to-Host transfer-bytes @ 64 = if
    EP1-to-Host 0 usb-send-data-packet
   else
    false EP1-to-Host endpoint-busy? !
   then
  else
   ep1-start-ring-transfer-to-host
  then
;

: usb-control-c-handler ( -- )

  [: cr ." USB Control-C pressed - rebooting" cr ;] usb-core-debug
 
  usb-control-keys-enabled? @ if

   usb-set-modem-offline  \ inform client we are going offline
   20 ms
   \ usb-core::usb-remove-device      \ pull device and let host handle removal and re-insertion
   20 ms
   reboot

  else
   \ ignore control-c and carry on receiving data
   false EP1-to-Pico endpoint-busy? !
  then
;

: usb-control-t-handler ( -- )

  [: cr ." USB Control-T pressed - attention" cr ;] usb-core-debug

  attention? @ if
   [: attention-hook @ execute ;] try    
    ?raise
  else
    [: attention-start-hook @ execute ;] try  \ not working - ask Travis
    ?raise
  then

  drop
;

\ add EP1 received data to rx ring buffer
: ep1-handler-to-pico ( -- )
  EP1-to-Pico usb-update-transfer-bytes
  EP1-to-Pico usb-toggle-data-pid
  0 control-key !
  EP1-to-Pico transfer-bytes @ 0 ?do
   EP1-to-Pico dpram-address @ i + c@ { rx-byte }
   rx-byte ctrl-c = if rx-byte control-key ! then
   rx-byte ctrl-t = if rx-byte control-key ! then
   rx-byte write-rx   
  loop
  
  control-key @ ctrl-c = if usb-control-c-handler then
  control-key @ ctrl-t = if usb-control-t-handler then
  
  rx-free 63 > if
   EP1-to-Pico 64 usb-receive-data-packet
  else
   false EP1-to-Pico endpoint-busy? !
  then
;

: ep3-handler-to-host ( -- )
  EP3-to-Host usb-toggle-data-pid
  false EP3-to-Host endpoint-busy? !
  \ nothing else to do here for line state nofification
;

: usb-buffer-status-control-endpoints ( -- )
  USB_BUFFER_STATUS @ USB_BUFFER_STATUS_EP0_TO_HOST and if ep0-handler-to-host then
  USB_BUFFER_STATUS @ USB_BUFFER_STATUS_EP0_TO_PICO and if ep0-handler-to-pico then
;

: usb-buffer-status-function-endpoints ( -- )
  \ clear all buffer status IRQ in advance for any read-blocking Linux clients (e.g. Minicom)
  USB_BUFFER_STATUS @ dup { buffer-status } USB_BUFFER_STATUS !
  buffer-status USB_BUFFER_STATUS_EP1_TO_HOST and if ep1-handler-to-host then
  buffer-status USB_BUFFER_STATUS_EP1_TO_PICO and if ep1-handler-to-pico then
  buffer-status USB_BUFFER_STATUS_EP3_TO_HOST and if ep3-handler-to-host then
;

: usb-handle-buffer-status ( -- )
  \ EP0-to-Host and/or EP0-to-Pico
  USB_BUFFER_STATUS @ USB_BUFFER_STATUS_EP0 and if
   usb-buffer-status-control-endpoints
  else
   usb-buffer-status-function-endpoints
  then
;

\ general USB interrupt handler
: usb-irq-handler ( -- )
  USB_INTS @ { ints }
  ints USB_INTS_START_OF_FRAME and if usb-handle-start-of-frame then
  ints USB_INTS_BUFFER_STATUS and if usb-handle-buffer-status then
  ints USB_INTS_SETUP_REQ and if usb-handle-setup-packet then
  ints USB_INTS_BUS_RESET and if usb-handle-bus-reset then
;

: usb-insert-device ( -- )
  USB_INTS_BUS_RESET              USB_INTE bis!
  USB_INTS_SETUP_REQ              USB_INTE bis!
  USB_INTS_BUFFER_STATUS          USB_INTE bis!
  USB_SIE_CTRL_EP0_INT_1BUF       USB_SIE_CONTROL bis!
  USB_SIE_CTRL_PULLUP_EN          USB_SIE_CONTROL bis!
;

: usb-remove-device ( -- )
  USB_INTS_BUS_RESET              USB_INTE bic!
  USB_INTS_SETUP_REQ              USB_INTE bic!
  USB_INTS_BUFFER_STATUS          USB_INTE bic!
  USB_INTS_START_OF_FRAME         USB_INTE bic!
  USB_SIE_CTRL_PULLUP_EN          USB_SIE_CONTROL bic!
  USB_SIE_CTRL_EP0_INT_1BUF       USB_SIE_CONTROL bic!
  USB_MAIN_CTRL_CONTROLLER_EN     USB_MAIN_CONTROL bic!
;

\ Get DTR (Data Terminal Ready) signal setting from host client
: usb-dtr? ( -- dtr? )
  DTR? @
;

\ Initialize USB
: init-usb ( -- )
  init-port-signals
  reset-usb-hardware
  usb-init-setup-packet
  0 sof-callback-handler !
  USB_DPRAM_Base dpram-size 0 fill
  init-usb-default-endpoints
  false usb-device-configured? !

  ['] usb-irq-handler usbctrl-vector vector!

  \ $00 usbctrl-irq NVIC_IPR_IP!  \ give USB highest IRQ priority -
  \ may be required in some future scenarios - heavy timer or other IRQ use ?

  USB_USB_MUXING_TO_PHY                 USB_USB_MUXING bis!
  USB_USB_MUXING_SOFTCON                USB_USB_MUXING bis!
  USB_USB_PWR_VBUS_DETECT               USB_USB_POWER bis!
  USB_USB_PWR_VBUS_DETECT_OVERRIDE_EN   USB_USB_POWER bis!

  rp2350? if                            \ clear bit to remove isolation
   USB_MAIN_CTRL_PHY_ISOLATE             USB_MAIN_CONTROL bic!
  then

  USB_MAIN_CTRL_CONTROLLER_EN           USB_MAIN_CONTROL bis!

  usbctrl-irq NVIC_ISER_SETENA!
;

end-module

compile-to-ram

\ USB_3_CDC_CORE (JANUARY 2025) END ===============================================