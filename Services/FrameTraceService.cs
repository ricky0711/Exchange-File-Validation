using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>
/// Part B — resolves the layered FACE model for a demand: <b>signal → Contained I-PDU (+byte/bit) →
/// container frame (original vs gateway Tx) → Network-Path synthesis route</b>, as an ordered trace.
/// </summary>
public sealed class FrameTraceService
{
    public FrameTrace Build(IsrDemand d, ReferenceData data)
    {
        var t = new FrameTrace();
        var sig = d.MatchedDef;
        if (sig is null) return t;

        // 1) Signal layer — where the signal sits inside which I-PDU / frame.
        var pos = (sig.BytePosition.Length > 0 || sig.BitPosition.Length > 0)
            ? $"byte {Or(sig.BytePosition, "?")} / bit {Or(sig.BitPosition, "?")}"
            : "position n/a";
        t.Steps.Add(new TraceStep("Signal", sig.SignalName,
            $"I-PDU {Or(sig.PduName, "?")} @ {pos} → frame {Or(sig.FrameName, "?")} [{Or(sig.FrameType, "?")}]"));

        // 2) Assembly layer — the container that packs this I-PDU.
        var cont = ResolveContainer(sig, data);
        if (cont is not null)
        {
            t.HasContainer = true;
            t.Steps.Add(new TraceStep("Container",
                cont.FrameName + (cont.IsSecured ? "  (secured, MAC=x)" : ""),
                $"emitted by {Or(cont.TxUnit, "?")} (gateway) · source ECU {Or(cont.OriginalTxUnit, "?")} · packs I-PDU {Or(cont.ContainedPdu, "?")}"));
        }

        // 3) Routing layer — Network-Path synthesis for this frame/PDU + Tx + Rx.
        var route = ResolveRoute(d, sig, cont, data);
        if (route is not null)
        {
            t.CrossesGateway = CrossesGateway(route.SynthesisPath);
            t.Steps.Add(new TraceStep("Route",
                $"{Or(route.Transmitter, d.Emitter)} → {Or(route.Receiver, d.Receiver)}",
                route.SynthesisPath.Length > 0 ? route.SynthesisPath : "(route exists, no synthesis text)"));
        }
        else
        {
            t.Steps.Add(new TraceStep("Route", $"{d.Emitter} → {d.Receiver}",
                "No Network-Path row — new gateway routing may be required."));
        }

        // 4) Container decision (ASIL) — only meaningful for ASIL requests or signals already in a container.
        var asil = AsilDetector.Detect(d.LossLinkageAsil, d.CorruptDataAsil);
        if (asil != AsilState.None || sig.IsContainerFrame)
        {
            var decision = new ContainerDecisionService().Decide(asil == AsilState.Requested, t.CrossesGateway, sig.IsFd && !t.CrossesGateway);
            string asilTxt = asil switch
            {
                AsilState.Requested => "ASIL requested",
                AsilState.Undetermined => "ASIL undetermined",
                _ => "no ASIL"
            };
            string decTxt = asil == AsilState.Undetermined
                ? "undetermined (ASIL incomplete)"
                : $"{decision.Needed} ({decision.Reason})";
            t.Steps.Add(new TraceStep("Decision", $"Container: {decTxt}",
                $"{asilTxt} · {(t.CrossesGateway ? "crosses CGW/PIU" : "single channel")}"));
        }
        return t;
    }

    /// <summary>The container frame that packs this signal's I-PDU (by PDU, then by frame/container name).</summary>
    public static ContainerFrame? ResolveContainer(SignalDef sig, ReferenceData data)
    {
        if (sig.PduName.Length > 0 && data.ContainersByPdu.TryGetValue(sig.PduName, out var byPdu) && byPdu.Count > 0)
            return byPdu.FirstOrDefault(c => Same(c.FrameName, sig.FrameName) || Same(c.FrameName, sig.FrameContainer)) ?? byPdu[0];
        if (sig.FrameContainer.Length > 0 && data.ContainersByFrame.TryGetValue(sig.FrameContainer, out var cf)) return cf;
        if (data.ContainersByFrame.TryGetValue(sig.FrameName, out var cf2)) return cf2;

        // "all PDU" carries the Frame Container linkage directly — synthesize a minimal container when the
        // Construction sheet doesn't provide one (master Tx = T-marked ECU on the container frame; secured = *SC_FD).
        var containerName = sig.FrameContainer.Length > 0 ? sig.FrameContainer
                          : (sig.IsContainerFrame ? sig.FrameName : "");
        if (containerName.Length > 0)
        {
            string master = data.SignalsByFrame.TryGetValue(containerName, out var fsigs)
                ? string.Join(", ", fsigs.SelectMany(s => s.Transmitters).Distinct(StringComparer.OrdinalIgnoreCase))
                : "";
            return new ContainerFrame
            {
                FrameName = containerName,
                ContainedPdu = sig.PduName,
                TxUnit = master,
                Mac = containerName.Contains("SC_FD", StringComparison.OrdinalIgnoreCase) ? "x" : "",
            };
        }
        return null;
    }

    /// <summary>Best Network-Path row for the demand. Matches on both the original ECU and the container gateway Tx (Part E).</summary>
    public static NetworkRoute? ResolveRoute(IsrDemand d, SignalDef? sig, ContainerFrame? cont, ReferenceData data)
    {
        if (data.Routes.Count == 0) return null;
        var pdu = sig?.PduName ?? "";
        var frame = sig?.FrameName ?? "";
        var rx = (d.Receiver ?? "").Trim();

        var txs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { (d.Emitter ?? "").Trim() };
        if (sig is not null) foreach (var t in sig.Transmitters) txs.Add(t);   // the matched frame's T-column ECU(s)
        if (cont is not null)
        {
            if (cont.OriginalTxUnit.Length > 0) txs.Add(cont.OriginalTxUnit);
            if (cont.TxUnit.Length > 0) txs.Add(cont.TxUnit);
        }

        bool FrameMatch(NetworkRoute r) =>
            (pdu.Length > 0 && r.PduName.Equals(pdu, StringComparison.OrdinalIgnoreCase)) ||
            (frame.Length > 0 && r.FrameName.Equals(frame, StringComparison.OrdinalIgnoreCase));

        var exact = data.Routes.FirstOrDefault(r => FrameMatch(r)
            && txs.Contains(r.Transmitter.Trim())
            && r.Receiver.Trim().Equals(rx, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return data.Routes.FirstOrDefault(r => FrameMatch(r) && r.Receiver.Trim().Equals(rx, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Does the synthesis route cross the Central Gateway / PIU master?</summary>
    public static bool CrossesGateway(string synthesis)
        => synthesis.Contains("PIU", StringComparison.OrdinalIgnoreCase)
        || synthesis.Contains("CGW", StringComparison.OrdinalIgnoreCase)
        || synthesis.Contains("Gateway", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string a, string b) => a.Length > 0 && b.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    private static string Or(string v, string fallback) => v.Length > 0 ? v : fallback;
}
