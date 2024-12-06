using SuperSimpleTcp;
using System;

namespace ChessServer
{
    public class ChessServer
    {
        private SimpleTcpServer server;

        public ChessServer(string ipAddress, int port)
        {
            // Khởi tạo server với địa chỉ IP và cổng
            server = new SimpleTcpServer($"{ipAddress}:{port}");

            // Đăng ký sự kiện cho server
            server.Events.ClientConnected += ClientConnected;
            server.Events.ClientDisconnected += ClientDisconnected;
            server.Events.DataReceived += DataReceived;
        }

        public void Start()
        {
            server.Start();
            Console.WriteLine("Server đã khởi động...");
        }

        public void Stop()
        {
            server.Stop();
            Console.WriteLine("Server đã dừng...");
        }

        // Sự kiện khi client kết nối
        private void ClientConnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine($"Client kết nối: {e.IpPort}");
        }

        // Sự kiện khi client ngắt kết nối
        private void ClientDisconnected(object sender, ConnectionEventArgs e)
        {
            Console.WriteLine($"Client ngắt kết nối: {e.IpPort}");
        }

        // Sự kiện khi nhận dữ liệu từ client
        private void DataReceived(object sender, DataReceivedEventArgs e)
        {
            string data = System.Text.Encoding.UTF8.GetString(e.Data);
            Console.WriteLine($"Dữ liệu nhận từ {e.IpPort}: {data}");

            // Gửi lại phản hồi cho client
            server.Send(e.IpPort, $"Server nhận: {data}");
        }
    }
}