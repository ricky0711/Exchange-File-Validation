using ClosedXML.Excel;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var sampleDir = Path.Combine(repoRoot, "SampleData");
Directory.CreateDirectory(sampleDir);

var msgSetPath = Path.Combine(sampleDir, "MsgSet_Sample.xlsx");
var isrAppliedPath = Path.Combine(sampleDir, "ISR_Applied_Sample.xlsx");
var exchangePath = Path.Combine(sampleDir, "ExchangeFile_Sample.xlsm");

Console.WriteLine("Generating sample Excel files in:");
Console.WriteLine($"  {sampleDir}");
Console.WriteLine();

GenerateMsgSet(msgSetPath);
GenerateIsrApplied(isrAppliedPath);
GenerateExchangeFile(exchangePath);

Console.WriteLine("Created:");
Console.WriteLine($"  {Path.GetFileName(msgSetPath)}");
Console.WriteLine($"  {Path.GetFileName(isrAppliedPath)}");
Console.WriteLine($"  {Path.GetFileName(exchangePath)}");
Console.WriteLine();
Console.WriteLine("Use these three files in the app Load bar (Windows only).");
Console.WriteLine("For production validation, replace with your real Exchange File, Msg Set, and ISR Applied workbooks.");

static void GenerateMsgSet(string path)
{
    using var wb = new XLWorkbook();

    var ml = wb.Worksheets.Add("Message List all PDU");
    ml.Cell(1, 1).Value = "Signal Name";
    ml.Cell(1, 2).Value = "Frame Name";
    ml.Cell(1, 3).Value = "Frame ID (Hex)";
    ml.Cell(1, 4).Value = "Frame Type";
    ml.Cell(1, 5).Value = "Frame Container";
    ml.Cell(1, 6).Value = "Contained I-PDU Name";
    ml.Cell(1, 7).Value = "Byte Position (0-7)";
    ml.Cell(1, 8).Value = "Bit Position (7-0)";
    ml.Cell(1, 9).Value = "Signal Size (Bits)";
    ml.Cell(1, 10).Value = "Value Type (Sign)";
    ml.Cell(1, 11).Value = "Frame Size";
    ml.Cell(1, 12).Value = "Coding (Bin/Hex)";
    ml.Cell(1, 13).Value = "Meaning";
    ml.Cell(1, 14).Value = "Unit";
    ml.Cell(1, 15).Value = "Unavailable Value (Bin/Hex)";
    ml.Cell(1, 16).Value = "Resolution (Dec)";
    ml.Cell(1, 17).Value = "Min (Dec)";
    ml.Cell(1, 18).Value = "Max (Dec)";
    ml.Cell(1, 19).Value = "Transmission Type";
    ml.Cell(1, 20).Value = "Period (ms)";
    ml.Cell(1, 21).Value = "Functional";
    ml.Cell(1, 22).Value = "BCM";
    ml.Cell(1, 23).Value = "PCM";

    ml.Cell(2, 1).Value = "VehicleSpeed";
    ml.Cell(2, 2).Value = "BCM_A01_FD";
    ml.Cell(2, 3).Value = "0x123";
    ml.Cell(2, 4).Value = "CAN FD";
    ml.Cell(2, 5).Value = "";
    ml.Cell(2, 6).Value = "BCM_A01_PDU";
    ml.Cell(2, 7).Value = "0";
    ml.Cell(2, 8).Value = "7";
    ml.Cell(2, 9).Value = "16";
    ml.Cell(2, 10).Value = "Unsigned";
    ml.Cell(2, 11).Value = "8";
    ml.Cell(2, 12).Value = "0x0000";
    ml.Cell(2, 13).Value = "speed";
    ml.Cell(2, 14).Value = "km/h";
    ml.Cell(2, 15).Value = "0xFFFF";
    ml.Cell(2, 16).Value = "1";
    ml.Cell(2, 17).Value = "0";
    ml.Cell(2, 18).Value = "255";
    ml.Cell(2, 19).Value = "Periodic";
    ml.Cell(2, 20).Value = "100";
    ml.Cell(2, 21).Value = "x";
    ml.Cell(2, 22).Value = "T";
    ml.Cell(2, 23).Value = "R";

    ml.Cell(3, 1).Value = "CRC_BCM_A01";
    ml.Cell(3, 2).Value = "BCM_A01_FD";
    ml.Cell(3, 6).Value = "BCM_A01_PDU";
    ml.Cell(3, 9).Value = "16";
    ml.Cell(3, 22).Value = "T";

    ml.Cell(4, 1).Value = "Clock_BCM_A01";
    ml.Cell(4, 2).Value = "BCM_A01_FD";
    ml.Cell(4, 6).Value = "BCM_A01_PDU";
    ml.Cell(4, 9).Value = "16";
    ml.Cell(4, 22).Value = "T";

    var dico = wb.Worksheets.Add("Dico");
    dico.Cell(1, 1).Value = "ECU Msg Set";
    dico.Cell(1, 2).Value = "Code";
    dico.Cell(1, 3).Value = "ISR Status Name";
    dico.Cell(1, 4).Value = "Different";
    dico.Cell(2, 1).Value = "BCM";
    dico.Cell(2, 2).Value = "01";
    dico.Cell(2, 3).Value = "BCM";
    dico.Cell(3, 1).Value = "PCM";
    dico.Cell(3, 2).Value = "02";
    dico.Cell(3, 3).Value = "PCM";

    var routes = wb.Worksheets.Add("Network Path");
    routes.Cell(1, 1).Value = "PDU Name";
    routes.Cell(1, 2).Value = "Frame Name";
    routes.Cell(1, 3).Value = "Transmitter";
    routes.Cell(1, 4).Value = "Receiver";
    routes.Cell(1, 5).Value = "V1-CAN";
    routes.Cell(1, 6).Value = "Synthesis";
    routes.Cell(2, 1).Value = "BCM_A01_PDU";
    routes.Cell(2, 2).Value = "BCM_A01_FD";
    routes.Cell(2, 3).Value = "BCM";
    routes.Cell(2, 4).Value = "PCM";
    routes.Cell(2, 5).Value = "x";
    routes.Cell(2, 6).Value = "BCM => V1-CAN => PCM";

    var construction = wb.Worksheets.Add("Construction of Container frame");
    construction.Cell(4, 1).Value = "frame name";
    construction.Cell(4, 2).Value = "Contained I-PDU name";
    construction.Cell(4, 3).Value = "Tx unit";
    construction.Cell(5, 1).Value = "BCM_A01_FD";
    construction.Cell(5, 2).Value = "BCM_A01_PDU";
    construction.Cell(5, 3).Value = "BCM";

    wb.SaveAs(path);
}

static void GenerateIsrApplied(string path)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("Nissan_FACE HS applied proposal");
    ws.Cell(1, 1).Value = "ISR N°";
    ws.Cell(1, 2).Value = "Electronic Feature";
    ws.Cell(1, 3).Value = "Transmitter";
    ws.Cell(1, 4).Value = "Receiver";
    ws.Cell(1, 5).Value = "Frame";
    ws.Cell(1, 6).Value = "Parameter";
    ws.Cell(1, 7).Value = "ASIL Level";
    ws.Cell(1, 8).Value = "CLK";
    ws.Cell(1, 9).Value = "CRC";
    ws.Cell(1, 10).Value = "T1_2025";

    ws.Cell(2, 1).Value = "10001";
    ws.Cell(2, 2).Value = "F001";
    ws.Cell(2, 3).Value = "BCM";
    ws.Cell(2, 4).Value = "PCM";
    ws.Cell(2, 5).Value = "BCM_A01_FD";
    ws.Cell(2, 6).Value = "VehicleSpeed";
    ws.Cell(2, 7).Value = "QM";
    ws.Cell(2, 10).Value = "x";

    wb.SaveAs(path);
}

static void GenerateExchangeFile(string path)
{
    using var wb = new XLWorkbook();
    var ws = wb.Worksheets.Add("ExchangeFile");
    ws.Cell(1, 1).Value = "ISR_Number";
    ws.Cell(1, 2).Value = "Feature_Number";
    ws.Cell(1, 3).Value = "EmitterCode";
    ws.Cell(1, 4).Value = "Emitter";
    ws.Cell(1, 5).Value = "ReceiverCode";
    ws.Cell(1, 6).Value = "Receiver";
    ws.Cell(1, 7).Value = "Frame";
    ws.Cell(1, 8).Value = "ParameterProposal";
    ws.Cell(1, 9).Value = "Media Type";
    ws.Cell(1, 10).Value = "NetworkType";
    ws.Cell(1, 11).Value = "Synthesis_Status";
    ws.Cell(1, 12).Value = "UpdateTime";
    ws.Cell(1, 13).Value = "LogicalData";
    ws.Cell(1, 14).Value = "AnalogData";
    ws.Cell(1, 15).Value = "KindOfIsr";
    ws.Cell(1, 16).Value = "OtherRequirements";
    ws.Cell(1, 17).Value = "LossLinkageASIL";
    ws.Cell(1, 18).Value = "CorruptDataASIL";

    ws.Cell(2, 1).Value = "20001";
    ws.Cell(2, 2).Value = "F002";
    ws.Cell(2, 3).Value = "01";
    ws.Cell(2, 4).Value = "BCM";
    ws.Cell(2, 5).Value = "02";
    ws.Cell(2, 6).Value = "PCM";
    ws.Cell(2, 7).Value = "BCM_A01_FD";
    ws.Cell(2, 8).Value = "VehicleSpeed";
    ws.Cell(2, 9).Value = "CAN FD";
    ws.Cell(2, 10).Value = "CAN FD";
    ws.Cell(2, 11).Value = "New";
    ws.Cell(2, 12).Value = "2025-01";
    ws.Cell(2, 13).Value = "";
    ws.Cell(2, 14).Value = "Unit=km/h; Min=0; Max=255; Resolution=1";
    ws.Cell(2, 15).Value = "Analog";
    ws.Cell(2, 16).Value = "Tx: BCM\nRx: PCM\nUnavailableValue: 0xFFFF\nNetworkPath: BCM => V1-CAN => PCM";
    ws.Cell(2, 17).Value = "QM";
    ws.Cell(2, 18).Value = "";

    ws.Cell(3, 1).Value = "30001";
    ws.Cell(3, 2).Value = "F003";
    ws.Cell(3, 3).Value = "01";
    ws.Cell(3, 4).Value = "BCM";
    ws.Cell(3, 5).Value = "02";
    ws.Cell(3, 6).Value = "PCM";
    ws.Cell(3, 7).Value = "";
    ws.Cell(3, 8).Value = "BrandNewSignal";
    ws.Cell(3, 9).Value = "CAN FD";
    ws.Cell(3, 10).Value = "CAN FD";
    ws.Cell(3, 11).Value = "New";
    ws.Cell(3, 12).Value = "2025-01";
    ws.Cell(3, 13).Value = "[Etat_0: Off]\n[Etat_1: On]";
    ws.Cell(3, 14).Value = "";
    ws.Cell(3, 15).Value = "Logical";
    ws.Cell(3, 16).Value = "Tx: BCM\nRx: PCM";

    wb.SaveAs(path);
}
