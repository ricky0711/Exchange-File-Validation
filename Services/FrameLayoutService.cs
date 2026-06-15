using ExchangeFileValidator.Models;

namespace ExchangeFileValidator.Services;

/// <summary>One contiguous run of a signal within a single byte row (for the bit/byte grid).</summary>
public sealed class FrameCellSegment
{
    public int Byte { get; init; }
    public int ColStart { get; init; }   // 0 = bit 7 (MSB), 7 = bit 0 (LSB)
    public int Span { get; init; }
}

/// <summary>A signal placed on the frame layout.</summary>
public sealed class SignalBlock
{
    public SignalDef Sig { get; init; } = null!;
    public string Name => Sig.SignalName;
    public int StartByte { get; init; }
    public int StartBit { get; init; }
    public int Size { get; init; }
    public int ColorIndex { get; init; }              // PDU band group (for colouring application signals)
    public string Kind { get; init; } = "app";        // app | crc | clock | filler
    public List<FrameCellSegment> Segments { get; } = new();
}

/// <summary>A contained-I-PDU band (byte range) inside a container frame.</summary>
public sealed class PduBand
{
    public string Pdu { get; init; } = "";
    public int StartByte { get; init; }
    public int ByteCount { get; init; }
    public int ColorIndex { get; init; }
}

public sealed class FrameLayout
{
    public string FrameName { get; init; } = "";
    public string FrameId { get; init; } = "";
    public string FrameType { get; init; } = "";
    public string Tx { get; init; } = "";
    public bool IsContainer { get; init; }
    public bool IsSecured { get; init; }
    public int ByteCount { get; init; }
    public List<SignalBlock> Blocks { get; } = new();
    public List<PduBand> Bands { get; } = new();
    public int UsedBits { get; init; }
    public int TotalBits => ByteCount * 8;
    public int FreeBits => Math.Max(0, TotalBits - UsedBits);
    public bool HasFrame => FrameName.Length > 0;
    public string Header => $"{FrameName}   ·   ID {(FrameId.Length > 0 ? FrameId : "—")}   ·   {(FrameType.Length > 0 ? FrameType : "—")}"
        + (IsSecured ? "  (secured)" : "") + $"   ·   {ByteCount} bytes   ·   Tx {(Tx.Length > 0 ? Tx : "—")}";
    public string Usage => $"{UsedBits}/{TotalBits} bits used · {FreeBits} free";
}

/// <summary>
/// Feature 2 — builds the byte × bit layout of a frame from the already-loaded Message-List rows.
/// Bit numbering is the sheet's "7 → 0" per byte; multi-byte signals are placed Motorola/MSB-first
/// (start bit = MSB, descending; continuing at bit 7 of the next byte). ASSUMPTION: Motorola/MSB —
/// if the file proves Intel/LSB it's a one-place change in <see cref="Segments"/>.
/// </summary>
public sealed class FrameLayoutService
{
    public FrameLayout Build(string frameName, ReferenceData data)
    {
        if (!data.SignalsByFrame.TryGetValue(frameName, out var sigs) || sigs.Count == 0)
            return new FrameLayout { FrameName = frameName };

        var rep = sigs[0];
        bool isContainer = data.ContainersByFrame.ContainsKey(frameName) || rep.IsContainerFrame;
        bool isSecured = rep.IsSecuredContainer || frameName.Contains("SC_FD", StringComparison.OrdinalIgnoreCase);
        string tx = data.ContainersByFrame.TryGetValue(frameName, out var cont) && cont.TxUnit.Length > 0
            ? cont.TxUnit
            : string.Join(", ", sigs.SelectMany(s => s.Transmitters).Distinct(StringComparer.OrdinalIgnoreCase));

        // PDU bands (byte ranges) + a colour index per PDU.
        var pduOrder = sigs.Select(s => s.PduName).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int ColorOf(string pdu) { int i = pduOrder.FindIndex(p => p.Equals(pdu, StringComparison.OrdinalIgnoreCase)); return i < 0 ? 0 : i; }

        int observedMaxByte = 0;
        var blocks = new List<SignalBlock>();
        int usedBits = 0;
        foreach (var s in sigs)
        {
            try
            {
                if (!int.TryParse(s.BytePosition.Trim(), out var startByte)) continue;
                if (!int.TryParse(s.BitPosition.Trim(), out var startBit)) continue;
                int size = s.SignalSizeBits ?? 0;
                if (size <= 0 || startBit < 0 || startBit > 7 || startByte < 0) continue;

                string kind = s.IsFiller ? "filler" : s.IsCrc ? "crc" : s.IsClock ? "clock" : "app";
                var block = new SignalBlock
                {
                    Sig = s, StartByte = startByte, StartBit = startBit, Size = size,
                    Kind = kind, ColorIndex = ColorOf(s.PduName),
                };

                // Motorola/MSB segmentation across byte boundaries.
                int remaining = size, byteIdx = startByte, bit = startBit;
                while (remaining > 0)
                {
                    int lo = Math.Max(0, bit - remaining + 1);
                    int span = bit - lo + 1;
                    block.Segments.Add(new FrameCellSegment { Byte = byteIdx, ColStart = 7 - bit, Span = span });
                    remaining -= span;
                    observedMaxByte = Math.Max(observedMaxByte, byteIdx);
                    byteIdx++; bit = 7;
                    if (byteIdx > 64) break;   // safety
                }
                if (kind != "filler") usedBits += size;
                blocks.Add(block);
            }
            catch { /* one malformed row must not blank the whole view */ }
        }

        // Frame length: explicit Frame Size if present, else observed extent, else type default (CAN=8, FD≤64).
        int declared = sigs.Select(s => s.FrameSize).FirstOrDefault(v => v is > 0) ?? 0;
        int typeDefault = rep.IsFd || isContainer ? 64 : 8;
        int byteCount = declared > 0 ? declared : Math.Max(observedMaxByte + 1, rep.IsFd || isContainer ? observedMaxByte + 1 : 8);
        byteCount = Math.Clamp(byteCount == 0 ? typeDefault : byteCount, 1, 64);

        var layout = new FrameLayout
        {
            FrameName = frameName, FrameId = rep.FrameIdHex, FrameType = rep.FrameType, Tx = tx,
            IsContainer = isContainer, IsSecured = isSecured, ByteCount = byteCount, UsedBits = usedBits,
        };
        layout.Blocks.AddRange(blocks);

        // bands from signal byte ranges grouped by PDU
        foreach (var pdu in pduOrder)
        {
            var ps = sigs.Where(s => s.PduName.Equals(pdu, StringComparison.OrdinalIgnoreCase)
                                   && int.TryParse(s.BytePosition.Trim(), out _)).ToList();
            if (ps.Count == 0) continue;
            int minB = ps.Min(s => int.Parse(s.BytePosition.Trim()));
            int maxB = ps.Max(s => int.Parse(s.BytePosition.Trim()));
            layout.Bands.Add(new PduBand { Pdu = pdu, StartByte = minB, ByteCount = maxB - minB + 1, ColorIndex = ColorOf(pdu) });
        }
        return layout;
    }
}
