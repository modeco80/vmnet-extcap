// This file is a part of vmnet-excap.
// SPDX-License-Identifier: MIT

using ExtcapNet.Config;
using ExtcapNet.CaptureInterface;
using ExtcapNet.PacketPublish;
using VMNetExtcap;
using ExtcapNet;

static void VMWarePacketProducer(uint selectedVMnet, Dictionary<ConfigField, string> config, IPacketsPublisher publisher) {
	var packetData = new byte[0x640];
	using var vmNetUserInterface = new VMnetUserInterface();
	var captureStartTime = DateTime.Now;

	vmNetUserInterface.RequestVMnet(selectedVMnet);

	uint packetLen = 0;
	while (true) {
		try {
			packetLen = vmNetUserInterface.CapturePacket(packetData);
		} catch (EndOfCaptureException) {
			break;
		}

		// The only way the packet length can be 0 is if the event
		// wait timed out. Therefore, in that case, we just try again.
		if (packetLen == 0)
			continue;

		publisher.Send(new PacketToSend {
			LinkLayer = LinkLayerType.Ethernet,
			Data = new ArraySegment<byte>(packetData, 0, (int)packetLen).ToArray(),
			TimeFromCaptureStart = DateTime.Now.Subtract(captureStartTime)
		});
	}
}

var extcap = new ExtcapManager();

// Add VMnet interfaces
foreach (var id in Enumerable.Range(0, 20)) {
	var bci = new BasicCaptureInterface(
		displayName: string.Format("VMware VMnet{0}", id),
		producer: (config, publisher) => {
			VMWarePacketProducer((uint)id, config, publisher);
			return;
		},
		defaultLinkLayer: LinkLayerType.Ethernet
	);

	extcap.RegisterInterface(bci);
}

extcap.Run(args);
