namespace TealDocAiTool.Common;

/// <summary>
/// Időkorláttal futtat egy szinkron, MEGSZAKÍTHATATLAN műveletet — konkrétan a Word COM-hívást.
/// A COM-nak nincs CancellationToken-je: ha a Word láthatatlan modális ablakot nyit (sérült fájl,
/// jelszókérés, konverziós kérdés), a hívás sosem tér vissza. Egy valódi, éles futás pontosan így
/// állt meg 863 fájl után. Dedikált háttérszálat használunk: ha az beragad, nem tartja életben
/// az appot, a köteg viszont megy tovább.
/// </summary>
public static class TimeLimited
{
    /// <returns><c>true</c>, ha a művelet az időkorláton belül BEFEJEZŐDÖTT (akár kivétellel,
    /// akár null eredménnyel); <c>false</c> KIZÁRÓLAG időtúllépéskor. A hívó erre a különbségre
    /// épít: a hibázó fájl csak egy hiba, a beragadt Word viszont az egész köteget veszélyezteti.</returns>
    public static bool TryRun<T>(Func<T> work, TimeSpan timeout, out T? result)
    {
        result = default;
        T? captured = default;

        var worker = new Thread(() =>
        {
            try { captured = work(); }
            catch { /* a hívó üres eredményt lát — a kinyerési út a kivételt amúgy is null-ként kezeli */ }
        })
        {
            IsBackground = true,
            Name = "TealDoc-idokorlatos-kinyeres",
        };

        worker.Start();
        if (!worker.Join(timeout)) return false;

        result = captured;
        return true;
    }
}
