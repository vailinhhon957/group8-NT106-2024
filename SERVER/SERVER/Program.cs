using chess;
using System;
using System.Threading.Tasks;

namespace chess // Thay "YourNamespace" bằng namespace của bạn
{
    class Program
    {
        static async Task Main(string[] args)
        {
     
            int port = 8080; // Cổng mà server lắng nghe
            TCPServer server = new TCPServer(port);  // Khởi tạo server với cổng 9000
            await server.StartAsync();
        }
    }
}