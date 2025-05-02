# vmnet-extcap

This is a [Wireshark extcap](https://www.wireshark.org/docs/man-pages/extcap.html) plugin which allows capturing packets from a VMware VMnet. 

VMware Workstation (other products too?) come with a "vnetsniffer" utility which can output old-style pcap 2.4 to a file, however this extcap doesn't need any temporary file anything.

It is currently Windows only. I don't think this utility is directly needed for Linux (at least, since I think you can persuade vmware to attach taps to a vmnet besides the "host adapter", and setting up a airgapped network is also much easier/actually possible), but
I'll consider porting it to Linux if I have to. (That will unfortunately probably mean porting the extcap library I used, since it is currently Very Windows only.)

# Okay, but what does this allow me to do?

Typically, if you wanted to capture packets from a VMnet, you would need to tell VMware to connect a virtual "Host Adapter",
to your.. well, host. Or use the vnetsniffer utility, which has its own cons:
- vnetsniffer will only write to a disk file.. at least, I think; it uses the weird Microsoft (airquotes) POSIX `_open/_read/_write()` calls.
- It kind of sucks on its own

This is fine in most cases, but in my slightly contrived use-case (handling live malware samples), attaching a host adapter is a unacceptably risky move, and vnetsniffer, as mentioned before, sucks.

This utility allows capturing packets without either vnetsniffer *or* risky manuvers like the host adapter.

# Building

You need .NET SDK or whatever

then just uhh `dotnet publish -r win-x64 /p:Configuration=Release -o out/`