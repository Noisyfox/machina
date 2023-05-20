// Copyright © 2021 Ravahn - All Rights Reserved
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY. without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see<http://www.gnu.org/licenses/>.

using System.Diagnostics;
using System.IO;
using System.Reflection;
using Machina.FFXIV.Memory;

namespace Machina.FFXIV.Oodle
{
    public static class OodleFactory
    {
        private static IOodleNative _oodleNative;
        private static OodleImplementation _oodleImplementation;

        private static readonly object _lock = new object();
        private const string _oodleLibAutoDetectName = "oo2net_9_win64.dll";
        private static bool _oodleLibAutoDetectEnabled = true;
        private static bool _oodleLibAutoDetected = false;

        public static void SetSuggestedImplementation(OodleImplementation implementation, string path)
        {
            lock (_lock)
            {
                _oodleImplementation = implementation;
                if (implementation == OodleImplementation.FfxivTcp || implementation == OodleImplementation.FfxivUdp || implementation == OodleImplementation.KoreanFfxivUdp)
                {
                    // Only try auto detect if the only other option is to use FFXIV impl
                    if (_oodleLibAutoDetected)
                    {
                        return;
                    }

                    if (_oodleLibAutoDetectEnabled)
                    {
                        // First unload any previous set impl
                        _oodleNative?.UnInitialize();
                        _oodleNative = null;

                        // Try oodle lib from common locations
                        var o = new OodleNative_Library();
                        foreach (string p in new[]
                                 {
                                     Path.Combine(Path.GetDirectoryName(new System.Uri(Assembly.GetEntryAssembly().CodeBase).LocalPath), "Plugins", "FFXIV_ACT_Plugin", _oodleLibAutoDetectName),
                                 })
                        {
                            if (File.Exists(p))
                            {
                                Trace.WriteLine($"{nameof(OodleFactory)}: Found oddle dll at {p}, loading...", "FOX-DEBUG-MACHINA");

                                o.Initialize(p);
                                if (o.Initialized)
                                {
                                    // Success!
                                    _oodleLibAutoDetected = true;
                                    _oodleNative = o;
                                    Trace.WriteLine($"{nameof(OodleFactory)}: Oddle dll {p} successfully loaded", "FOX-DEBUG-MACHINA");
                                    return;
                                }

                                Trace.WriteLine($"{nameof(OodleFactory)}: Oddle dll {p} failed to load", "FOX-DEBUG-MACHINA");
                            }
                        }

                        // Cannot auto detect
                        _oodleLibAutoDetectEnabled = false;
                        Trace.WriteLine($"{nameof(OodleFactory)}: Oddle dll load failed, fallback to game executable...", "FOX-DEBUG-MACHINA");
                    }
                }
                else
                {
                    // Reset auto detect state
                    _oodleLibAutoDetected = false;
                    _oodleLibAutoDetectEnabled = true;
                }

                // Note: Do not re-initialize if not changing implementation type.
                if (implementation == OodleImplementation.LibraryTcp || implementation == OodleImplementation.LibraryUdp)
                {
                    if (!(_oodleNative is OodleNative_Library))
                        _oodleNative?.UnInitialize();
                    else
                        return;
                    _oodleNative = new OodleNative_Library();
                }
                else
                {
                    if (!(_oodleNative is OodleNative_Ffxiv))
                        _oodleNative?.UnInitialize();
                    else
                        return;

                    // Control signatures for Korean FFXIV Oodle implementation
                    if (implementation == OodleImplementation.KoreanFfxivUdp)
                        _oodleNative = new OodleNative_Ffxiv(new KoreanSigScan());
                    else
                        _oodleNative = new OodleNative_Ffxiv(new SigScan());

                }
                _oodleNative.Initialize(path);
            }
        }

        public static IOodleWrapper Create()
        {
            lock (_lock)
            {
                if (_oodleNative is null)
                    return null;

                if (_oodleImplementation == OodleImplementation.FfxivTcp || _oodleImplementation == OodleImplementation.LibraryTcp)
                    return new OodleTCPWrapper(_oodleNative);
                else
                    return new OodleUDPWrapper(_oodleNative);
            }
        }
    }
}
