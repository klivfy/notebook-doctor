using System.Text;

namespace NotebookDoctor.Services;

/// <summary>
/// Leva 12.3, tarefa 14: QR Code feito aqui mesmo, sem biblioteca de fora (ISO/IEC 18004): modo byte (UTF-8), correção de
/// erro nível M e versões 1 a 10 (até 213 bytes, de sobra para "WIFI:...;;"). Escolhe a menor versão que cabe e a máscara
/// com menor penalidade, como manda a norma. O resultado é a matriz de módulos [linha, coluna], true = escuro.
/// </summary>
public static class QrCode
{
    // Nível M, versões 1 a 10: palavras de correção por bloco e quantidade de blocos
    private static readonly int[] EccPerBlock = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] Blocks = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];
    private static readonly int[][] Align =
    [
        [], [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50],
    ];

    public const int MaxVersion = 10;

    /// <summary>Palavras que cabem na versão (dados e correção), pela área livre da matriz.</summary>
    private static int RawCodewords(int v)
    {
        int bits = (16 * v + 128) * v + 64;
        if (v >= 2)
        {
            int n = v / 7 + 2;
            bits -= (25 * n - 10) * n - 55;
            if (v >= 7) bits -= 36;
        }
        return bits / 8;
    }

    private static int DataCodewords(int v) => RawCodewords(v) - EccPerBlock[v] * Blocks[v];

    /// <summary>A matriz do QR Code, ou null quando o texto não cabe na versão 10.</summary>
    public static bool[,]? Encode(string text)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        int version = 0;
        for (int v = 1; v <= MaxVersion; v++)
        {
            int countBits = v <= 9 ? 8 : 16;
            if (4 + countBits + data.Length * 8 <= DataCodewords(v) * 8) { version = v; break; }
        }
        if (version == 0) return null;

        // Bits: modo byte (0100), tamanho, os bytes, terminador, completar o byte e preencher com 0xEC 0x11
        var bits = new List<bool>();
        void Append(int value, int count) { for (int i = count - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0); }
        Append(0b0100, 4);
        Append(data.Length, version <= 9 ? 8 : 16);
        foreach (byte b in data) Append(b, 8);
        int capacity = DataCodewords(version) * 8;
        Append(0, Math.Min(4, capacity - bits.Count));
        Append(0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);
        var codewords = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        var all = AddEccAndInterleave(codewords, version);
        int size = version * 4 + 17;
        var m = new bool[size, size];
        var fn = new bool[size, size];
        DrawFunctionPatterns(m, fn, version);
        DrawCodewords(m, fn, all);

        int bestMask = 0, bestPenalty = int.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(m, fn, mask);
            DrawFormat(m, fn, mask);
            int p = Penalty(m);
            if (p < bestPenalty) { bestPenalty = p; bestMask = mask; }
            ApplyMask(m, fn, mask); // desfaz (XOR)
        }
        ApplyMask(m, fn, bestMask);
        DrawFormat(m, fn, bestMask);
        return m;
    }

    // ---------- correção de erro (Reed-Solomon em GF(256), polinômio 0x11D) ----------

    private static int Mul(int x, int y)
    {
        int z = 0;
        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return z;
    }

    private static byte[] Divisor(int degree)
    {
        var r = new byte[degree];
        r[degree - 1] = 1;
        int root = 1;
        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < r.Length; j++)
            {
                r[j] = (byte)Mul(r[j], root);
                if (j + 1 < r.Length) r[j] ^= r[j + 1];
            }
            root = Mul(root, 0x02);
        }
        return r;
    }

    private static byte[] Remainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var r = new byte[divisor.Length];
        foreach (byte b in data)
        {
            int factor = b ^ r[0];
            Array.Copy(r, 1, r, 0, r.Length - 1);
            r[^1] = 0;
            for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(divisor[i], factor);
        }
        return r;
    }

    private static byte[] AddEccAndInterleave(byte[] data, int v)
    {
        int numBlocks = Blocks[v], ecc = EccPerBlock[v], raw = RawCodewords(v);
        int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
        var div = Divisor(ecc);
        var blocks = new List<byte[]>();
        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int len = shortLen - ecc + (i < numShort ? 0 : 1);
            var dat = data.AsSpan(k, len);
            k += len;
            var block = new byte[shortLen + 1];
            dat.CopyTo(block);
            Remainder(dat, div).CopyTo(block, shortLen + 1 - ecc);
            blocks.Add(block);
        }
        var result = new List<byte>();
        for (int i = 0; i < shortLen + 1; i++)
            for (int j = 0; j < blocks.Count; j++)
                if (i != shortLen - ecc || j >= numShort) result.Add(blocks[j][i]); // o byte extra só existe nos blocos longos
        return [.. result];
    }

    // ---------- padrões fixos ----------

    private static void Set(bool[,] m, bool[,] fn, int x, int y, bool dark)
    {
        m[y, x] = dark;
        fn[y, x] = true;
    }

    private static void DrawFunctionPatterns(bool[,] m, bool[,] fn, int v)
    {
        int size = m.GetLength(0);
        for (int i = 0; i < size; i++) { Set(m, fn, 6, i, i % 2 == 0); Set(m, fn, i, 6, i % 2 == 0); }
        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    int d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(m, fn, x, y, d != 2 && d != 4);
                }
        var pos = Align[v];
        for (int i = 0; i < pos.Length; i++)
            for (int j = 0; j < pos.Length; j++)
            {
                if (i == 0 && j == 0 || i == 0 && j == pos.Length - 1 || i == pos.Length - 1 && j == 0) continue; // cantos dos localizadores
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Set(m, fn, pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        DrawFormat(m, fn, 0); // reserva as áreas (o valor certo entra depois da máscara)
        if (v >= 7)
        {
            int rem = v;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = v << 12 | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = ((bits >> i) & 1) != 0;
                int a = size - 11 + i % 3, b = i / 3;
                Set(m, fn, a, b, bit);
                Set(m, fn, b, a, bit);
            }
        }
    }

    /// <summary>Informação de formato: nível M (00) e a máscara, com BCH e o XOR 0x5412 da norma.</summary>
    private static void DrawFormat(bool[,] m, bool[,] fn, int mask)
    {
        int size = m.GetLength(0);
        int data = 0b00 << 3 | mask;
        int rem = data;
        for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        int bits = (data << 10 | rem) ^ 0x5412;
        bool Bit(int i) => ((bits >> i) & 1) != 0;
        for (int i = 0; i <= 5; i++) Set(m, fn, 8, i, Bit(i));
        Set(m, fn, 8, 7, Bit(6));
        Set(m, fn, 8, 8, Bit(7));
        Set(m, fn, 7, 8, Bit(8));
        for (int i = 9; i < 15; i++) Set(m, fn, 14 - i, 8, Bit(i));
        for (int i = 0; i < 8; i++) Set(m, fn, size - 1 - i, 8, Bit(i));
        for (int i = 8; i < 15; i++) Set(m, fn, 8, size - 15 + i, Bit(i));
        Set(m, fn, 8, size - 8, true); // módulo escuro fixo
    }

    private static void DrawCodewords(bool[,] m, bool[,] fn, byte[] data)
    {
        int size = m.GetLength(0), i = 0;
        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5; // a coluna do padrão de tempo não leva dados
            for (int vert = 0; vert < size; vert++)
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;
                    if (fn[y, x] || i >= data.Length * 8) continue;
                    m[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                    i++;
                }
        }
    }

    private static void ApplyMask(bool[,] m, bool[,] fn, int mask)
    {
        int size = m.GetLength(0);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (fn[y, x]) continue;
                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) m[y, x] = !m[y, x];
            }
    }

    // ---------- penalidade da máscara ----------

    private static readonly bool[] FinderA = [true, false, true, true, true, false, true, false, false, false, false];
    private static readonly bool[] FinderB = [false, false, false, false, true, false, true, true, true, false, true];

    internal static int Penalty(bool[,] m)
    {
        int size = m.GetLength(0), p = 0, dark = 0;
        for (int pass = 0; pass < 2; pass++)
            for (int a = 0; a < size; a++)
            {
                int run = 1;
                for (int b = 1; b <= size; b++)
                {
                    bool same = b < size && Get(a, b) == Get(a, b - 1);
                    if (same) run++;
                    else { if (run >= 5) p += 3 + run - 5; run = 1; }
                }
                for (int b = 0; b + 11 <= size; b++)
                {
                    bool ma = true, mb = true;
                    for (int k = 0; k < 11; k++) { bool v = Get(a, b + k); ma &= v == FinderA[k]; mb &= v == FinderB[k]; }
                    if (ma) p += 40;
                    if (mb) p += 40;
                }
                bool Get(int line, int pos) => pass == 0 ? m[line, pos] : m[pos, line];
            }
        for (int y = 0; y + 1 < size; y++)
            for (int x = 0; x + 1 < size; x++)
            {
                bool c = m[y, x];
                if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) p += 3;
            }
        foreach (bool c in m) if (c) dark++;
        int total = size * size;
        int k2 = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
        return p + Math.Max(0, k2) * 10;
    }

    /// <summary>Texto do QR Code que o celular entende para entrar numa rede Wi-Fi ("WIFI:T:WPA;S:Casa;P:senha;;").</summary>
    public static string WifiPayload(string ssid, string password, string auth)
    {
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace(":", "\\:").Replace("\"", "\\\"");
        string type = auth.StartsWith("WEP", StringComparison.OrdinalIgnoreCase) ? "WEP" : password.Length == 0 ? "nopass" : "WPA";
        return type == "nopass" ? $"WIFI:T:nopass;S:{Esc(ssid)};;" : $"WIFI:T:{type};S:{Esc(ssid)};P:{Esc(password)};;";
    }
}
