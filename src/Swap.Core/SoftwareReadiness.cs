// Copyright © 2026 KraicDev. SPDX-License-Identifier: GPL-3.0-or-later
namespace Swap.Core;

// KraicDev. Everything around the driver that a graphics card and its games commonly need, read without changing anything.
// Each finding says what was found; "Attention" marks what is missing or worth a look, never something Swap changes on its own.
public sealed record SoftwareFacts(
    bool SuiteInstalled, string SuiteVersion,
    bool VcRuntimeX64, bool VcRuntimeX86, string VcRuntimeVersion,
    bool DirectX9Legacy, bool HdmiAudio, bool PhysX,
    int HardwareScheduling, bool PendingRestart);

public static class SoftwareReadiness
{
    public static IReadOnlyList<HealthFinding> Evaluate(Vendor vendor, SoftwareFacts facts)
    {
        var findings = new List<HealthFinding>();
        if (vendor is Vendor.Nvidia or Vendor.Amd or Vendor.Intel)
            findings.Add(facts.SuiteInstalled ? new("suiteInstalled", VendorSuite.Name(vendor) + (facts.SuiteVersion.Length > 0 ? " " + facts.SuiteVersion : ""))
                                              : new("suiteMissing", VendorSuite.Name(vendor), true));
        // Most games ship with the Visual C++ runtime they need; when both are present nothing else is required.
        findings.Add(facts.VcRuntimeX64 && facts.VcRuntimeX86 ? new("vcRuntimeOk", facts.VcRuntimeVersion)
                   : new("vcRuntimeMissing", string.Join(" + ", new[] { facts.VcRuntimeX64 ? "" : "x64", facts.VcRuntimeX86 ? "" : "x86" }.Where(s => s.Length > 0)), true));
        // The DirectX 9.0c (June 2010) components are only needed by older games, so their absence is informative, not an error.
        findings.Add(new(facts.DirectX9Legacy ? "directx9Ok" : "directx9Missing", "d3dx9_43.dll"));
        findings.Add(facts.HdmiAudio ? new("hdmiAudioOk", "") : new("hdmiAudioMissing", "", vendor != Vendor.Intel));
        if (vendor == Vendor.Nvidia) findings.Add(new(facts.PhysX ? "physxOk" : "physxMissing", ""));
        if (facts.HardwareScheduling > 0) findings.Add(new(facts.HardwareScheduling == 2 ? "hagsOn" : "hagsOff", ""));
        if (facts.PendingRestart) findings.Add(new("restartPending", "", true));
        return findings;
    }
}
