//
// (C) 2006-2007 The SharpOS Project Team (http://www.sharpos.org)
//
// Authors:
//	Mircea-Cristian Racasan <darx_kies@gmx.net>
//
// Licensed under the terms of the GNU GPL v3,
//  with Classpath Linking Exception for Libraries
//

using System.Runtime.InteropServices;
using SharpOS.AOT.Attributes;
using SharpOS.Kernel.ADC;

namespace InternalSystem {
	[StructLayout (LayoutKind.Sequential)]
	[TargetNamespace ("System")]
	public abstract class Array: InternalSystem.Object {
		[StructLayout (LayoutKind.Sequential)]
		internal struct BoundEntry {
			internal int LowerBound;
			internal int Length;
		}

		internal int Rank;
		internal BoundEntry FirstEntry;
	}
}
