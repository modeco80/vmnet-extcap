// This file is a part of vmnet-excap.
// SPDX-License-Identifier: MIT

using ExtcapNet.Config;
using ExtcapNet.CaptureInterface;
using ExtcapNet.PacketPublish;
using VMNetExtcap;
using ExtcapNet;
using System.Globalization;

static uint ParseVMnet(string vmnet) {
	if (!vmnet.StartsWith("VMnet"))
		throw new InvalidOperationException("fucking idiot");

	return uint.Parse(vmnet.Substring(5), NumberStyles.Integer);
}

static void VMWarePacketProducer(Dictionary<ConfigField, string> config, IPacketsPublisher publisher) {
	var packetData = new byte[0x640];
	using var vmNetUserInterface = new VMnetUserInterface();
	// yeah, I don't understand this library's design choice for this either
	var selectedVMnet = config.Single(kvp => kvp.Key.DisplayName == "VMnet to capture").Value;
	var captureStartTime = DateTime.Now;

	vmNetUserInterface.RequestVMnet(ParseVMnet(selectedVMnet));

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

// Main
var extcap = new ExtcapManager();

var bci = new BasicCaptureInterface(displayName: "VMWare VMnet",
	producer: VMWarePacketProducer,
	defaultLinkLayer: LinkLayerType.Ethernet);


bci.AddConfigField(new MultiOptionsField(
	DisplayName: "VMnet to capture",
	ConfigField.FieldType.Selector,
	// I am aware this is batshit insane, but it's less batshit insane
	// than having to manually do this.
	Enumerable.Repeat("VMnet", 20)
		.Zip(Enumerable.Range(0, 20))
		.Select((a) => {
			var vmnetName = string.Format("{0}{1}", a.First, a.Second);
			return new ConfigOption(vmnetName, vmnetName);
		}).ToList(),
	Required: true
));

extcap.RegisterInterface(bci);

extcap.Run(args);
