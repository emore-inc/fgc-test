﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Security.Permissions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#pragma warning disable 618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, Flags = SecurityPermissionFlag.Execution)]
#pragma warning restore 618
#if (STANDALONEBUILD)
[assembly: AssemblyVersion(Microsoft.Build.Shared.MSBuildConstants.CurrentAssemblyVersion)]
[assembly: InternalsVisibleTo("Microsoft.Build.Utilities.UnitTests")]
#else
[assembly: InternalsVisibleTo("Microsoft.Build.Utilities.Unittest, PublicKey=002400000480000094000000060200000024000052534131000400000100010007d1fa57c4aed9f0a32e84aa0faefd0de9e8fd6aec8f87fb03766c834c99921eb23be79ad9d5dcc1dd9ad236132102900b723cf980957fc4e177108fc607774f29e8320e92ea05ece4e821c0a5efe8f1645c4c0c93c1ab99285d622caa652c1dfad63d745d6f2de5f17e5eaf0fc4963d261c8a12436518206dc093344d5ad293")]
[assembly: InternalsVisibleTo("Microsoft.Build.Utilities.Whidbey.Unittest, PublicKey=002400000480000094000000060200000024000052534131000400000100010007d1fa57c4aed9f0a32e84aa0faefd0de9e8fd6aec8f87fb03766c834c99921eb23be79ad9d5dcc1dd9ad236132102900b723cf980957fc4e177108fc607774f29e8320e92ea05ece4e821c0a5efe8f1645c4c0c93c1ab99285d622caa652c1dfad63d745d6f2de5f17e5eaf0fc4963d261c8a12436518206dc093344d5ad293")]
#endif
// This will enable passing the SafeDirectories flag to any P/Invoke calls/implementations within the assembly, 
// so that we don't run into known security issues with loading libraries from unsafe locations 
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

// Sbs2m8ypBTMmBsvKpYVmfRTs6aUqWfik5E_ANfGRt0B7J6-efgihw8typiOnYVuvByeX34GyTKeTJP8soLkNKncnBG_sxXyP4kMC73AnzQrzUAP_-qsXC_M
