// This file is a part of vmnet-excap.
// SPDX-License-Identifier: MIT

using Windows.Win32.Foundation;
using Win32API = Windows.Win32.PInvoke;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Storage.FileSystem;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace VMNetExtcap
{

	internal enum VNetUser_RequestType : uint { 
		Invalid = 0,
		VMnet = 1, // VMnet[X]
		PVN = 2 
	};

	/// <summary>
	/// Struct provided to driver via IOCTL to request a particular vmnet instance
	/// </summary>
	[StructLayout(LayoutKind.Sequential)]
	internal unsafe struct VNETUser_Request {
		public uint version;
		public VNetUser_RequestType requestType;

		// This is the ID of the vmnet you want to requet
		// Used if RequestType == VMnet
		public uint vmnetId;
		
		// Whatever thee are is used for RequestType == PVN
		public fixed uint pvnSrc[4];
		public fixed uint pvnDst[4]; // Not used, just to make sure its happy

		public VNETUser_Request() {
			version = 1;
			requestType = VNetUser_RequestType.Invalid;
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
		internal const uint VNET_REQUEST_IOCTL = 0x81022044;

		// in: uint* const (9. Could be multifunction and this means "init capture or something")
		// out: nothing
		internal const uint VNET_UNK1_IOCTL = 0x81022010;

		// in: HANDLE* const (should be a handle to an event created via CreateEvent)
		// out: nothing
		//
		// This could actually be "init capture" tbh
		internal const uint VNET_GIVE_EVENT_IOCTL = 0x8102202c;
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

			if (Marshal.SizeOf<VNETUser_Request>() != 0x2c) {
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

		public void RequestVMnet(uint vmnetNumber) {
			uint unsignedIntOfUnknownSignificance = 9;
			uint dummyReturned = 0;

			VNETUser_Request request = new VNETUser_Request();
			request.requestType = VNetUser_RequestType.VMnet;
			request.vmnetId = vmnetNumber;

			unsafe {
				// Request the vmnet
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNET_REQUEST_IOCTL, &request, (uint)Marshal.SizeOf<VNETUser_Request>(), null, 0, &dummyReturned, null))
					throw new Win32Exception("Couldn't request VNET from vmnetuserif driver");

				// do the next IOCTL
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNET_UNK1_IOCTL, &unsignedIntOfUnknownSignificance, 4, null, 0, &dummyReturned, null))
					throw new Win32Exception("UNK1 ioctl failed");

				// Once we've gotten this far, it's time to create an event so that we can
				// sleep instead of constantly polling for a packet.
				hPacketRecievedEvent = Win32API.CreateEvent(null, false, false, (string?)null);
				if (hPacketRecievedEvent.IsInvalid) {
					throw new Win32Exception("Failed to create packet recieve event? How'd that happen.");
				}

				// Finally, give the driver the handle to the event we created,
				// so we know when packets are actually ready.
				nint handle = hPacketRecievedEvent.DangerousGetHandle();
				if (!Win32API.DeviceIoControl(hVmUser, Constants.VNET_GIVE_EVENT_IOCTL, &handle, (uint)sizeof(nint), null, 0, &dummyReturned, null))
					throw new Win32Exception("Could not give IOCTL to vmnetuserif");
			}
		}

		public uint CapturePacket(Span<byte> buffer) { 
			var waitResult = Win32API.WaitForSingleObject(hPacketRecievedEvent, 33);
			if (waitResult == WAIT_EVENT.WAIT_TIMEOUT) {
				// No packet obtained, but not an error. Just have to try again :)
				return 0;
			} else if (waitResult == WAIT_EVENT.WAIT_FAILED) {
				throw new InvalidOperationException("End of packet capture?");
			} else if (waitResult == WAIT_EVENT.WAIT_OBJECT_0) {
				// The driver has signaled to us that we do have a packet.
				// Let's read it.

				uint packetSize = 0;
				BOOL ok = false;
				unsafe {
					ok = Win32API.ReadFile(hVmUser, buffer, &packetSize, null);
					if (!ok) {
						throw new Win32Exception("Could not read captured packet");
					}

					if (packetSize == 0) {
						throw new EndOfCaptureException();
					}
				}

				return packetSize;
			}

			throw new InvalidOperationException("How did you get here?");
		}

		void IDisposable.Dispose() {
			if (disposed) {
				throw new ObjectDisposedException("Dispose");
			}

			hVmUser.Close();

			if (hPacketRecievedEvent != null) {
				if (!hPacketRecievedEvent.IsInvalid)
				{
					hPacketRecievedEvent.Close();
				}
			}

			disposed = true;
		}
	}
}
