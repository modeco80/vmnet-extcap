# vmnet-extcap

This is a [Wireshark extcap](https://www.wireshark.org/docs/man-pages/extcap.html) plugin which allows capturing packets from a VMware VMnet. 

VMware Workstation (other products too?) come with a "vnetsniffer" utility which can output old-style pcap 2.4 to a file, however this extcap doesn't need any temporary file anything.

It is currently Windows only. I don't think this utility is directly needed for Linux (at least, since I think you can persuade vmware to attach taps to a vmnet besides the "host adapter"), but
I'll consider porting it to Linux if I have to. (That will unfortunately probably mean porting the extcap library I used, since it is Very Windows only.)

# Building

You need .NET SDK or whatever

then just uhh `dotnet publish -r win-x64 /p:Configuration=Release -o out/`