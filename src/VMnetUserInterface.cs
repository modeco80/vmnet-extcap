// This file is a part of vmnet-extcap.
// SPDX-License-Identifier: MIT

using Windows.Win32.Foundation;
using Win32API = Windows.Win32.PInvoke;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Storage.FileSystem;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace VMNetExtcap
{

	internal enum VNetUserConnectType : uint { 
		Invalid = 0,
		VMnet = 1, // VMnet[X]
		PVN = 2 
	};

	/// <summary>
	/// Struct provided to driver via IOCTL to request a particular vmnet instance
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct VNETUserConnectRequest {
		public uint version;
		public VNetUserConnectType requestType;

		// This is the ID of the vmnet you want to requet
		// Used if RequestType == VMnet
		public uint vmnetId;
		
		// Whatever thee are is used for RequestType == PVN
		public fixed uint pvnSrc[4];
		public fixed uint pvnDst[4]; // Not used, just to make sure its happy

		public VNETUserConnectRequest() {
			version = 1;
			requestType = VNetUserConnectType.Invalid;
			vmnetId = 0xff_ff_ff_ff;
			for (var i = 0; i < 4; ++i) {
				pvnSrc[i] = 0;
				pvnDst[i] = 0;
			}
		}
	};

	internal class Constants
	{
		// in: pointer to VNETUser_Request structure
		// out: nothing
		internal const uint VNETUSERIF_CONNECT_IOCTL = 0x81022044;

		// in: uint* const (new flags to set)
		// out: nothing
		internal const uint VNETUSERIF_SETIFFLAGS_IOCTL = 0x81022010;

		// in: HANDLE* const (A handle to an event object. Create via CreateEvent)
		// out: nothing
		internal const uint VNETUSERIF_SETEVENT_IOCTL = 0x8102202c;
	}

	/// <summary>
	/// Exception used to indicate end of capture
	/// </summary>
	public class EndOfCaptureException : Exception {
		public EndOfCaptureException() : base("End of packet capture was signaled.") { }
	}

	/// <summary>
	/// Internal exception used to indicate pre-check failure.
	/// This exception class is "internal" because it is not intended to be caught.
	/// </summary>
	internal class PreCheckFailureException : Exception {
		public PreCheckFailureException() : base("Pre-check test failed.") { }
		public PreCheckFailureException(string message) : base(string.Format("Pre-check test \"{0}\" failed.", message)) { }
	}
	

	/// <summary>
	///  "Safe" wrapper to vmware vmnetuserif.
	/// </summary>
	public class VMnetUserInterface : IDisposable
	{
		private bool disposed = false;
		private SafeFileHandle hVmUser;
		private SafeFileHandle? hPacketRecievedEvent = null;

		// Pre-check to make sure the library will work.
		internal static bool PreCheckPerformed = false;
		private static void PreCheck() {
			// The precheck has succeeded already; we don't need to keep 
			// doing it over and over again.
			if (PreCheckPerformed) {
				return;
			}

			if (Marshal.SizeOf<VNETUserConnectRequest>() != 0x2c) {
				throw new PreCheckFailureException("VNETUser_Request size == 0x2c");
			}

			PreCheckPerformed = true;
		}

		public VMnetUserInterface() {
		   // Make sure that the library is actually safe to use.
		   PreCheck();

		   // Create a handle to the vmnetuserif driver.
		   hVmUser = Win32API.CreateFile(
				"\\\\.\\Global\\vmnetuserif",
				(uint)(GENERIC_ACCESS_RIGHTS.GENERIC_READ | GENERIC_ACCESS_RIGHTS.GENERIC_WRITE),
				FILE_SHARE_MODE.FILE_SHARE_NONE,
				null,
				FILE_CREATION_DISPOSITION.OPEN_EXISTING,
				FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
				null
			);

			if (hVmUser.IsInvalid) {
				throw new Win32Exception("Failed to open a handle to the vmnetuserif driver");
			}
		}

		/// <summary>
		/// Requests a particular VMnet network by VMnet index.
		/// </summary>
		/// <param name="vmnetIndex">The index.</param>
		/// <exception cref="Win32Exception">Thrown if DeviceIoControl() fails.</exception>
		public void RequestVMnet(uint vmnetIndex) {
			uint dummyReturned = 0;

			// Create the request structure.
			VNETUserConnectRequest request = new VNETUserConnectRequest();
			request.requestType = VNetUserConnectType.VMnet;
			request.vmnetId = vmnetIndex;

			unsafe {
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNETUSERIF_CONNECT_IOCTL, &request, (uint)Marshal.SizeOf<VNETUserConnectRequest>(), null, 0, &dummyReturned, null))
					throw new Win32Exception("Couldn't request VNET from vmnetuserif driver");
			}
		}

		public void SetInterfaceFlags(uint flags) {
			uint dummyReturned = 0;
			unsafe {
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNETUSERIF_SETIFFLAGS_IOCTL, &flags, (uint)Marshal.SizeOf<uint>(), null, 0, &dummyReturned, null))
					throw new Win32Exception("UNK1 ioctl failed");
			}
		}

		/// <summary>
		///  Begins capture of packets. RequestVMnet should have been called beforehand. 
		/// </summary>
		/// <exception cref="Win32Exception">Thrown if DeviceIoControl() fails.</exception>
		public void BeginPacketCapture() {
			uint dummyReturned = 0;

			unsafe {
				// If I had to guess, this probably sets some bit for
				// "promiscious" mode? Dunno.
				SetInterfaceFlags(9);

				// Once we've gotten this far, it's time to create an event so that we can
				// sleep instead of constantly polling for a packet.
				hPacketRecievedEvent = Win32API.CreateEvent(null, true, false, (string?)null);
				if (hPacketRecievedEvent.IsInvalid) {
					throw new Win32Exception("Failed to create packet recieve event? How'd that happen.");
				}

				// Finally, give the driver the handle to the event we created,
				// so we know when packets are actually ready.
				nint handle = hPacketRecievedEvent.DangerousGetHandle();
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNETUSERIF_SETEVENT_IOCTL, &handle, (uint)sizeof(nint), null, 0, &dummyReturned, null))
					throw new Win32Exception("Could not give IOCTL to vmnetuserif");
			}
		}

		/// <summary>
		/// Captures a single packet.
		/// BeginCapture() must have been called first.
		/// This function does not return until a packet has definitively been captured.
		/// </summary>
		/// <param name="buffer"></param>
		/// <returns>The size of the packet.</returns>
		/// <exception cref="Win32Exception">If ReadFile() fails.</exception>
		public uint CapturePacket(Span<byte> buffer) {
			uint packetSize = 0;

			// Wait forever for a packet
			var waitRes = Win32API.WaitForSingleObject(hPacketRecievedEvent, unchecked(0xff_ff_ff_ff));
			if (waitRes == WAIT_EVENT.WAIT_FAILED)
				throw new Win32Exception("Failed to wait for packet");

			unsafe {
				var ok = Win32API.ReadFile(hVmUser, buffer, &packetSize, null);
				if (!ok) {
					throw new Win32Exception("Could not read captured packet");
				}

				if (packetSize == 0) {
					throw new EndOfCaptureException();
				}
			}
			

			return packetSize;
		}

		void IDisposable.Dispose() {
			if (disposed) {
				throw new ObjectDisposedException("Dispose");
			}

			hVmUser.Close();

			if (hPacketRecievedEvent != null) {
				if (!hPacketRecievedEvent.IsInvalid) {
					hPacketRecievedEvent.Close();
				}
			}

			disposed = true;
			GC.SuppressFinalize(this);
		}
	}
}
