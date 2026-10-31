using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace GlassBoard;

// Shell extraction can involve slow third-party handlers. Never do it in a tile's UI build.
public static class BoardIconLoader
{
    private static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
    private static readonly object gate = new object();
    private static readonly ConcurrentDictionary<string, Task<ImageSource>> pending = new ConcurrentDictionary<string, Task<ImageSource>>();
    static BoardIconLoader()
    {
        var worker = new Thread(() => { foreach (var work in queue.GetConsumingEnumerable()) work(); })
        { IsBackground = true, Name = "GlassBoard icon loader" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }
    public static Task<ImageSource> ReadAsync(Entry entry, bool overlays, bool sharper)
    {
        var snapshot = new Entry { Path = entry.Path, IconPath = entry.IconPath, IsFolder = entry.IsFolder };
        string key = snapshot.Path + "\n" + snapshot.IconPath + "\n" + overlays + "/" + sharper;
        lock(gate) return pending.GetOrAdd(key, _ =>
        {
            var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Add(() =>
            {
                try { completion.TrySetResult(FeatureUi.Icon(snapshot, overlays, sharper)); }
                catch { completion.TrySetResult(null); }
                finally { lock(gate) pending.TryRemove(key, out var ignored); }
            });
            return completion.Task;
        });
    }
}
