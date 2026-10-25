using System;
using System.Collections.Generic;
using System.Linq;

namespace Lab07
{
    // ==========================================
    // 1. CÁC ĐỊNH NGHĨA CƠ BẢN & NGOẠI LỆ
    // ==========================================
    public enum DeviceStatus
    {
        Available,
        Borrowed,
        UnderMaintenance
    }

    public class DuplicateIdException : Exception
    {
        public string DuplicateId { get; }
        public DuplicateIdException(string id) : base($"Thiết bị có mã {id} đã tồn tại.")
        {
            DuplicateId = id;
        }
    }

    public class NoSuitableItemException : Exception
    {
        public NoSuitableItemException() : base("Không tìm thấy thiết bị nào phù hợp với yêu cầu.") { }
    }

    public class DeviceNotReadyException : Exception
    {
        public string DeviceId { get; }
        public DeviceStatus CurrentStatus { get; }
        public DeviceNotReadyException(string id, DeviceStatus status) 
            : base($"Thiết bị {id} không sẵn sàng. Trạng thái hiện tại: {status}")
        {
            DeviceId = id;
            CurrentStatus = status;
        }
    }

    public interface IIdentifiable
    {
        string Id { get; }
    }

    // ==========================================
    // 2. HỆ PHÂN CẤP THIẾT BỊ (ĐA HÌNH)
    // ==========================================
    public abstract class TeachingDevice : IIdentifiable
    {
        public string Id { get; }
        public string Name { get; }
        public string StorageLocation { get; }
        public DeviceStatus Status { get; protected set; }

        protected TeachingDevice(string id, string name, string storageLocation)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Mã không hợp lệ.");
            Id = id;
            Name = name;
            StorageLocation = storageLocation;
            Status = DeviceStatus.Available;
        }

        public abstract bool IsReadyForUse();
        public abstract double GetDefaultPriorityScore();

        public void Borrow()
        {
            if (!IsReadyForUse())
                throw new DeviceNotReadyException(Id, Status);
            Status = DeviceStatus.Borrowed;
        }

        public void Return()
        {
            if (Status != DeviceStatus.Borrowed)
                throw new InvalidOperationException("Chỉ có thể trả thiết bị đang được mượn.");
            Status = DeviceStatus.Available;
        }
    }

    public class ProjectorRemote : TeachingDevice
    {
        public int BatteryLevel { get; }

        public ProjectorRemote(string id, string name, string location, int battery) 
            : base(id, name, location)
        {
            BatteryLevel = battery;
        }

        public override bool IsReadyForUse()
        {
            return Status == DeviceStatus.Available && BatteryLevel >= 20;
        }

        public override double GetDefaultPriorityScore()
        {
            return BatteryLevel;
        }
    }

    public class AirConditionerRemote : TeachingDevice
    {
        public IReadOnlySet<string> CompatibleRooms { get; }

        public AirConditionerRemote(string id, string name, string location, HashSet<string> rooms) 
            : base(id, name, location)
        {
            CompatibleRooms = rooms;
        }

        public bool IsCompatibleWith(string roomId)
        {
            return CompatibleRooms.Contains(roomId);
        }

        public override bool IsReadyForUse()
        {
            return Status == DeviceStatus.Available;
        }

        public override double GetDefaultPriorityScore()
        {
            return 100;
        }
    }

    public class WirelessMicrophone : TeachingDevice
    {
        public int BatteryLevel { get; }
        public int SignalQuality { get; }

        public WirelessMicrophone(string id, string name, string location, int battery, int signal) 
            : base(id, name, location)
        {
            BatteryLevel = battery;
            SignalQuality = signal;
        }

        public override bool IsReadyForUse()
        {
            return Status == DeviceStatus.Available && BatteryLevel >= 30 && SignalQuality >= 40;
        }

        public override double GetDefaultPriorityScore()
        {
            return (BatteryLevel + SignalQuality) / 2.0;
        }
    }

    // ==========================================
    // 3. KHO LƯU TRỮ (GENERIC REPOSITORY)
    // ==========================================
    public interface IRepository<T> where T : IIdentifiable
    {
        void Add(T item);
        bool Remove(string id);
        T? FindById(string id);
        IReadOnlyList<T> GetAll();
        IReadOnlyList<T> FindAll(Predicate<T> condition);
    }

    public class InMemoryRepository<T> : IRepository<T> where T : IIdentifiable
    {
        private readonly Dictionary<string, T> _data = new Dictionary<string, T>();

        public void Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_data.ContainsKey(item.Id))
                throw new DuplicateIdException(item.Id);
            _data[item.Id] = item;
        }

        public bool Remove(string id)
        {
            return _data.Remove(id);
        }

        public T? FindById(string id)
        {
            return _data.TryGetValue(id, out var item) ? item : default;
        }

        public IReadOnlyList<T> GetAll()
        {
            return _data.Values.ToList().AsReadOnly();
        }

        public IReadOnlyList<T> FindAll(Predicate<T> condition)
        {
            return _data.Values.Where(item => condition(item)).ToList().AsReadOnly();
        }
    }

    // ==========================================
    // 4. CHIẾN LƯỢC LỰA CHỌN (STRATEGY PATTERN)
    // ==========================================
    public interface ISelectionStrategy<T>
    {
        T Select(IReadOnlyList<T> candidates);
    }

    public class HighestPriorityStrategy<T> : ISelectionStrategy<T>
    {
        private readonly Func<T, double> scoreSelector;

        public HighestPriorityStrategy(Func<T, double> scoreSelector)
        {
            this.scoreSelector = scoreSelector;
        }

        public T Select(IReadOnlyList<T> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                throw new NoSuitableItemException();
            return candidates.OrderByDescending(scoreSelector).First();
        }
    }

    public class FirstAvailableStrategy<T> : ISelectionStrategy<T>
    {
        public T Select(IReadOnlyList<T> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                throw new NoSuitableItemException();
            return candidates.First();
        }
    }

    // ==========================================
    // 5. DỊCH VỤ CẤP PHÁT TỔNG QUÁT (ALLOCATION)
    // ==========================================
    public class AllocationService<T> where T : TeachingDevice
    {
        private readonly IRepository<T> repository;
        private ISelectionStrategy<T> strategy;

        public AllocationService(IRepository<T> repository, ISelectionStrategy<T> strategy)
        {
            this.repository = repository;
            this.strategy = strategy;
        }

        public void ChangeStrategy(ISelectionStrategy<T> newStrategy)
        {
            this.strategy = newStrategy;
        }

        public T Allocate(Predicate<T> additionalCondition)
        {
            var candidates = repository.FindAll(device => 
                device.IsReadyForUse() && 
                (additionalCondition == null || additionalCondition(device)));

            var selected = strategy.Select(candidates);
            selected.Borrow();
            return selected;
        }
    }

    // ==========================================
    // 6. KIỂM THỬ (11 KỊCH BẢN YÊU CẦU)
    // ==========================================
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Setup Repositories
            var projRepo = new InMemoryRepository<ProjectorRemote>();
            var acRepo = new InMemoryRepository<AirConditionerRemote>();
            var micRepo = new InMemoryRepository<WirelessMicrophone>();

            // Khởi tạo thiết bị
            var p1 = new ProjectorRemote("PRJ01", "Projector 1", "Kho A", 100);
            var p2 = new ProjectorRemote("PRJ02", "Projector 2", "Kho A", 15); // Không đủ pin
            var p3 = new ProjectorRemote("PRJ03", "Projector 3", "Kho A", 85);

            var ac1 = new AirConditionerRemote("AC01", "AC 1", "Kho B", new HashSet<string> { "A201", "A202" });
            var ac2 = new AirConditionerRemote("AC02", "AC 2", "Kho B", new HashSet<string> { "A203" });

            var m1 = new WirelessMicrophone("MIC01", "Mic 1", "Kho C", 100, 90);
            var m2 = new WirelessMicrophone("MIC02", "Mic 2", "Kho C", 20, 50); // Không đủ pin
            var m3 = new WirelessMicrophone("MIC03", "Mic 3", "Kho C", 80, 80);

            // 1. Thêm thiết bị vào repository
            projRepo.Add(p1); projRepo.Add(p2); projRepo.Add(p3);
            acRepo.Add(ac1); acRepo.Add(ac2);
            micRepo.Add(m1); micRepo.Add(m2); micRepo.Add(m3);
            Console.WriteLine("1. Đã thêm thiết bị vào kho.");

            // 2. Thử thêm hai thiết bị trùng mã
            try { projRepo.Add(new ProjectorRemote("PRJ01", "Clone", "Kho A", 100)); }
            catch (DuplicateIdException ex) { Console.WriteLine($"2. Bắt lỗi trùng mã thành công: {ex.Message}"); }

            // 3. Tìm thiết bị theo mã
            var foundAc = acRepo.FindById("AC02");
            Console.WriteLine($"3. Tìm thiết bị: {foundAc?.Id} - {foundAc?.Name}");

            // 4. Lọc các thiết bị sẵn sàng
            var readyMics = micRepo.FindAll(m => m.IsReadyForUse());
            Console.WriteLine($"4. Số micro sẵn sàng (Pin >=30, Tín hiệu >=40): {readyMics.Count} (Kỳ vọng: 2)");

            // 5. Chọn điều khiển máy chiếu có điểm cao nhất
            var projStrategy = new HighestPriorityStrategy<ProjectorRemote>(d => d.GetDefaultPriorityScore());
            var projService = new AllocationService<ProjectorRemote>(projRepo, projStrategy);
            var allocatedProj = projService.Allocate(d => true);
            Console.WriteLine($"5. Chọn máy chiếu ưu tiên cao nhất: {allocatedProj.Id} (Pin: {allocatedProj.BatteryLevel})");

            // 6. Chọn điều khiển điều hòa tương thích với một phòng
            var acStrategy = new FirstAvailableStrategy<AirConditionerRemote>();
            var acService = new AllocationService<AirConditionerRemote>(acRepo, acStrategy);
            var allocatedAc = acService.Allocate(d => d.IsCompatibleWith("A203"));
            Console.WriteLine($"6. Chọn AC tương thích phòng A203: {allocatedAc.Id}");

            // 7. Chọn micro đầu tiên sẵn sàng
            var micStrategy = new FirstAvailableStrategy<WirelessMicrophone>();
            var micService = new AllocationService<WirelessMicrophone>(micRepo, micStrategy);
            var allocatedMic = micService.Allocate(d => true);
            Console.WriteLine($"7. Chọn Micro đầu tiên sẵn sàng: {allocatedMic.Id}");

            // 8. Đổi chiến lược trong thời gian chạy và quan sát
            micService.ChangeStrategy(new HighestPriorityStrategy<WirelessMicrophone>(d => d.GetDefaultPriorityScore()));
            var allocatedMic2 = micService.Allocate(d => true);
            Console.WriteLine($"8. Đổi sang chiến lược điểm cao nhất, chọn tiếp Micro: {allocatedMic2.Id}");

            // 9. Thử cấp phát khi không có ứng viên phù hợp
            try { acService.Allocate(d => d.IsCompatibleWith("A999")); }
            catch (NoSuitableItemException ex) { Console.WriteLine($"9. Bắt lỗi không có ứng viên phù hợp: {ex.Message}"); }

            // 10. Chứng minh thiết bị được chọn chuyển sang Borrowed
            Console.WriteLine($"10. Trạng thái của {allocatedProj.Id} sau khi cấp phát: {allocatedProj.Status} (Kỳ vọng: Borrowed)");

            // 11. Chứng minh repository tổng quát dùng chung mà không sao chép mã
            Console.WriteLine($"11. InMemoryRepository dùng chung cho {projRepo.GetType().GenericTypeArguments[0].Name} và {acRepo.GetType().GenericTypeArguments[0].Name} thành công.");
        }
    }
}
