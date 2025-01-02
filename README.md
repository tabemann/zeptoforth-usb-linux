# zeptoforth USB Linux
Linux-compatible USB CDC/ACM Serial stack for Zeptoforth on Raspberry RP2040 and RP2350 boards.

Includes support for Windows hosts and client applications.


* Tested with [multiple Linux and other clients](https://github.com/Serialcomms/zeptoforth-usb-linux/blob/main/documents/test_matrix.md)  

* Very good upload performance with [Minicom](https://github.com/Serialcomms/zeptoforth-usb-linux/blob/main/screenshots/Minicom_195000_CPS.png) and [GTKTerm](https://github.com/Serialcomms/zeptoforth-usb-linux/blob/main/screenshots/GTK_Term_timestamps.png) clients

* Install Zeptoforth full non-USB image to Pico first.

* Use zeptocomjs to upload usb files in numerical order, rebooting between each upload.

* Use file `large_upload_file_line_numbers.fs` to check USB upload performance.
