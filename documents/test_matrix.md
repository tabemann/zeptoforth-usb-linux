
|  Host O/S  |  Client Terminal   | Test | Note/s              | Comment                                 |
|:----------:|--------------------|:----:|---------------------|-----------------------------------------|
| Linux      | zeptocomjs/python  |  ok  |                     | javascript buffer size 65k              |
| Linux      | zeptocomjs/apache  |  ok  |                     | javascript buffer size 256k             |
| Linux      | VS Code / terminal |  ok  |                     |                                         |
| Linux      | Minicom            |  ok  | [^1] [^2] [^3] [^8] | 'Online' shown in status bar (from DCD) |
| Linux      | Picocom            |  ok  |                     | Exits on reboot                         |
| Linux      | e4thcom            |  ok  |                     | must set baud to 115200                 |
| Linux      | Cutecom            |  ok  |                     |                                         |
| Linux      | CoolTerm           |  ok  | [^4]                | no file drag & drop                     | 
| Linux      | GTKTerm            |  ok  | [^5] [^6] [^7] [^8] | reboot recovery (if configured)         | 
| Linux      | screen             |  ok  |                     |                                         |
| Linux      | tio                |  ok  |                     |                                         |
| Linux      | cu                 |  ok  |                     | exits on reboot                         |
| Windows    | PuTTY              |  ok  | [^8]                |                                         |
| Windows    | CoolTerm           |  ok  | [^4]                | file drag & drop (fast)                 |  


[^1]: fast ASCII file upload - Control-A > S > ascii
[^2]: fast reboot recovery / reconnect
[^3]: upload speeds in range 22000CPS to 195000CPS observed
[^4]: enter `false ack-nak-enabled !` to remove dots
[^5]: fast ASCII file upload - File > send RAW file
[^6]: modem and terminal signals shown in status bar
[^7]: set configuration > autoreconnect
[^8]: copy & paste to terminal window works
