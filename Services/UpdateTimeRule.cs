namespace ExchangeFileValidator.Services;

/// <summary>Port of Module7.CheckUpdateTime — validates UpdateTime against Period (BG) and Excl.Time (BH).</summary>
public static class UpdateTimeRule
{
    public static bool Check(string updateTime, string period, string exclTime)
    {
        try
        {
            updateTime = (updateTime ?? "").Trim();
            var bg = (period ?? "").Trim();
            var bh = (exclTime ?? "").Trim();

            bool hasEvent = updateTime.Contains("Event", StringComparison.OrdinalIgnoreCase);
            bool s1 = updateTime.Contains('+') && updateTime.Contains('(');
            bool s3b = hasEvent && updateTime.Contains('(') && !s1;
            bool s3a = hasEvent && !s3b && !s1;
            bool s2 = !hasEvent;

            double Paren()
            {
                int o = updateTime.IndexOf('('), c = updateTime.IndexOf(')');
                return (o >= 0 && c > o) ? Val(updateTime.Substring(o + 1, c - o - 1)) : 0;
            }
            bool Blank(string v) => v.Length == 0 || v == "-";

            if (s1)  // X + Event(Y)
            {
                double x = Val(updateTime.Split('+')[0]);
                double y = Paren();
                if (Blank(bg) || Blank(bh)) return false;
                return Val(bg) <= x && Val(bh) <= y;
            }
            if (s3b)  // ...Event...(Y), no '+'
            {
                double y = Paren();
                if (!Blank(bg)) return false;
                if (!double.TryParse(bh, out _)) return false;
                return Val(bh) <= y;
            }
            if (s3a)  // Event, no parens
                return Blank(bg) && Blank(bh);
            if (s2)   // plain X
            {
                double x = Val(updateTime);
                if (Blank(bg)) return false;
                return Val(bg) <= x;   // BH can be anything
            }
            return false;
        }
        catch { return false; }
    }

    private static double Val(string s)
    {
        var digits = new string((s ?? "").Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
        return double.TryParse(digits, out var v) ? v : 0;
    }
}
