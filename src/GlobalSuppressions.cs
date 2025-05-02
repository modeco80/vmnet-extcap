// This file is a part of vmnet-excap.
// SPDX-License-Identifier: MIT

// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "I will fix this properly if making this multiplatform is actually required; however on Linux I'm pretty sure this utility is unneeded.")]
