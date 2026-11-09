using Microsoft.Extensions.Caching.Memory;

namespace ScoutVN.Services
{
    /// <summary>
    /// "Phiên bản dữ liệu": tăng mỗi khi có thay đổi cầu thủ / đội / giải / trận / thống kê / tài khoản
    /// (AppDbContext.SaveChanges gọi Bump). Khóa bộ nhớ đệm gắn kèm phiên bản nên dữ liệu vừa nhập
    /// hiển thị ngay, không cần chờ hết hạn.
    /// </summary>
    public static class DataVersion
    {
        private static long _v = 1;
        public static long Current => Interlocked.Read(ref _v);
        public static void Bump() => Interlocked.Increment(ref _v);
    }

    /// <summary>Không lưu đệm (dùng khi khởi tạo dữ liệu lúc chạy lần đầu).</summary>
    public sealed class NoDataCache : IDataCache
    {
        public static readonly NoDataCache Instance = new();
        public Task<T> GetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) => factory();
    }

    public interface IDataCache
    {
        /// <summary>Lấy dữ liệu dùng chung từ bộ nhớ đệm; chưa có thì gọi <paramref name="factory"/> (chỉ một lần dù nhiều người cùng hỏi).</summary>
        Task<T> GetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null);
    }

    /// <summary>
    /// Bộ nhớ đệm cho các truy vấn tra cứu nặng dùng chung giữa mọi người dùng (danh sách cầu thủ + thống kê,
    /// bảng xếp hạng, định giá CLB...). Khi nhiều người cùng mở trang, CSDL chỉ bị truy vấn một lần
    /// thay vì mỗi người một lần → hệ thống không bị nghẽn.
    /// Dữ liệu trả về là bản dùng chung: chỉ đọc, không được sửa.
    /// </summary>
    public class DataCache : IDataCache
    {
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
        private readonly IMemoryCache _cache;
        private readonly object _gate = new();
        public DataCache(IMemoryCache cache) => _cache = cache;

        public async Task<T> GetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
        {
            var k = $"{key}@v{DataVersion.Current}";
            Lazy<Task<T>> lazy;
            lock (_gate)   // tránh hai yêu cầu cùng lúc chạy cùng một truy vấn nặng
            {
                if (!_cache.TryGetValue(k, out lazy!))
                {
                    lazy = new Lazy<Task<T>>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
                    _cache.Set(k, lazy, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl });
                }
            }
            try { return await lazy.Value; }
            catch { _cache.Remove(k); throw; }   // lỗi thì lần sau truy vấn lại
        }
    }
}
