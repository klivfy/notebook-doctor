using System.Runtime.InteropServices;
using System.Text;

namespace NotebookDoctor.Services;

public sealed class SqliteException(string message, int code) : IOException(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// SQLite pelo winsqlite3.dll, que já vem no Windows 10 e 11: nenhuma biblioteca a mais para distribuir.
/// Só o que a limpeza de navegadores precisa: abrir, executar comandos e ler linhas de texto.
/// </summary>
public sealed class SqliteDb : IDisposable
{
    private const int ReadOnlyFlag = 0x1, ReadWriteFlag = 0x2, CreateFlag = 0x4, Row = 100, Done = 101;
    private IntPtr _db;

    private SqliteDb(IntPtr db) => _db = db;

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public static SqliteDb Open(string path, bool readOnly = false, int busyMs = 3000, bool create = false)
    {
        int rc = sqlite3_open_v2(Utf8(path), out var db, readOnly ? ReadOnlyFlag : ReadWriteFlag | (create ? CreateFlag : 0), IntPtr.Zero);
        if (rc != 0)
        {
            string message = db != IntPtr.Zero ? Marshal.PtrToStringUTF8(sqlite3_errmsg(db)) ?? "" : "";
            if (db != IntPtr.Zero) sqlite3_close(db);
            throw new SqliteException($"{Path.GetFileName(path)}: {message} ({rc})", rc);
        }
        sqlite3_busy_timeout(db, busyMs);
        return new SqliteDb(db);
    }

    private SqliteException Error(int rc) => new(Marshal.PtrToStringUTF8(sqlite3_errmsg(_db)) ?? $"SQLite {rc}", rc);

    public void Execute(string sql)
    {
        int rc = sqlite3_exec(_db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out var err);
        if (rc == 0) return;
        string message = err != IntPtr.Zero ? Marshal.PtrToStringUTF8(err) ?? "" : "";
        if (err != IntPtr.Zero) sqlite3_free(err);
        throw new SqliteException($"{message} ({rc})", rc);
    }

    /// <summary>Todas as linhas, cada coluna como texto (nulo vira vazio).</summary>
    public List<string[]> Query(string sql)
    {
        int rc = sqlite3_prepare_v2(_db, Utf8(sql), -1, out var stmt, IntPtr.Zero);
        if (rc != 0) throw Error(rc);
        try
        {
            var rows = new List<string[]>();
            int columns = sqlite3_column_count(stmt);
            while ((rc = sqlite3_step(stmt)) == Row)
            {
                var row = new string[columns];
                for (int i = 0; i < columns; i++) row[i] = Marshal.PtrToStringUTF8(sqlite3_column_text(stmt, i)) ?? "";
                rows.Add(row);
            }
            if (rc != Done) throw Error(rc);
            return rows;
        }
        finally { sqlite3_finalize(stmt); }
    }

    public void Dispose()
    {
        if (_db == IntPtr.Zero) return;
        sqlite3_close(_db);
        _db = IntPtr.Zero;
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr p);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr stmt);
}
