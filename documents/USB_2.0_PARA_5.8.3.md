 ##
 5.8.3 Bulk Transfer Packet Size Constraints
 
 An endpoint for bulk transfers specifies the maximum data payload size that the endpoint can accept from
 or transmit to the bus.  The USB defines the allowable maximum bulk data payload sizes to be only 8, 16,
 32, or 64 bytes for full-speed endpoints and 512 bytes for high-speed endpoints. A low-speed device must
 not have bulk endpoints. This maximum applies to the data payloads of the data packets; i.e., the size
 specified is for the data field of the packet as defined in Chapter 8, not including other protocol-required
 information.
 
 A bulk endpoint is designed to support a maximum data payload size.  A bulk endpoint reports in its
 configuration information the value for its maximum data payload size.  The USB does not require that data
 payloads transmitted be exactly the maximum size; i.e., if a data payload is less than the maximum, it does
 not need to be padded to the maximum size.
 
 All Host Controllers are required to have support for 8-, 16-, 32-, and 64-byte maximum packet sizes for
 full-speed bulk endpoints and 512 bytes for high-speed bulk endpoints.  No Host Controller is required to
 support larger or smaller maximum packet sizes.
 
 During configuration, the USB System Software reads the endpoint’s maximum data payload size and
 ensures that no data payload will be sent to the endpoint that is larger than the supported size.
 An endpoint must always transmit data payloads with a data field less than or equal to the endpoint’s
 reported wMaxPacketSize value.  When a bulk IRP involves more data than can fit in one maximum-sized
 data payload, all data payloads are required to be maximum size except for the last data payload, which will
 contain the remaining data. 

 ##
 A bulk transfer is complete when the endpoint does one of the following:
 
 • Has transferred exactly the amount of data expected
 
 • Transfers a packet with a payload size less than wMaxPacketSize or <b>transfers a zero-length packet</b>
 ##
 
 When a bulk transfer is complete, the Host Controller retires the current IRP and advances to the next IRP.
 If a data payload is received that is larger than expected, all pending bulk IRPs for that endpoint will be
 aborted/retired
