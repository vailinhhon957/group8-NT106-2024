using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;
using System.Collections.Concurrent;
using System.Globalization;

namespace chess
{
    internal class TCPServer
    {
        public enum PieceColor
        {
            White,
            Black
        }

        private TcpListener tcpListener;
        private Queue<TcpClient> waitingPlayers = new Queue<TcpClient>();
        private const string connectionString = "Data Source=players.db;Version=3;";
        private List<GameRoom> gameRooms = new List<GameRoom>();
        private ConcurrentDictionary<string, TcpClient> activeSessions = new ConcurrentDictionary<string, TcpClient>();

        public TCPServer(int localPort)
        {
            tcpListener = new TcpListener(IPAddress.Any, localPort);
            CreateDatabase();
        }

        public async Task StartAsync()
        {
            tcpListener.Start();
            Console.WriteLine("Server started, waiting for connections...");

            while (true)
            {
                TcpClient client = await tcpListener.AcceptTcpClientAsync();
                Console.WriteLine($"Client connected: {client.Client.RemoteEndPoint}");
                _ = HandleClientAsync(client); // Xử lý mỗi client trong một task riêng biệt
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];

            try
            {
                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    Console.WriteLine($"Received: {request}");
                    string response = ProcessRequest(request, client);

                    byte[] responseData = Encoding.UTF8.GetBytes(response + "\n");
                    await stream.WriteAsync(responseData, 0, responseData.Length);
                }
            }
            catch (Exception ex)
            {
                string username = activeSessions.FirstOrDefault(x => x.Value == client).Key;

                if (string.IsNullOrEmpty(username))
                {
                    return;
                }
                bool removedFromActiveSessions = activeSessions.TryRemove(username, out _);
                if (removedFromActiveSessions)
                {
                    Console.WriteLine($"User {username} has been removed from active sessions.");
                }
                lock (gameRooms)
                {
                    var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
                    if (room != null)
                    {
                        room.RemovePlayer(client);
                        if (room.Player1 == null && room.Player2 == null)
                        {
                            gameRooms.Remove(room);
                            Console.WriteLine($"Game room {room.RoomId} has been removed.");
                        }
                    }
                }
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                if (client.Connected)
                {
                    client.Close();
                    RemovePlayer(client);
                }
                Console.WriteLine("Client disconnected.");
            }
        }

        private string ProcessRequest(string request, TcpClient client)
        {
            string[] parts = request.Split(' ');
            string command = parts[0].ToUpper(); // Chuyển sang chữ hoa để so sánh không phân biệt chữ hoa chữ thường
            string message = request.Substring(5);
            switch (command)
            {
                case "REGISTER":
                    return parts.Length >= 3 ? RegisterPlayer(parts[1], parts[2]) : "ERROR: Invalid request format";
                case "LOGIN":
                    return parts.Length >= 3 ? LoginPlayer(parts[1], parts[2], client) : "ERROR: Invalid request format";
                case "FIND_MATCH":
                    return FindMatch(client);
                case "CREATE_ROOM":
                    return parts.Length == 2 ? CreateRoom(parts[1], client) : "ERROR: Invalid request format";
                case "JOIN_ROOM":
                    return parts.Length == 2 ? JoinRoom(parts[1], client) : "ERROR: Invalid request format";
                case "MOVE":
                    return parts.Length >= 3 ? HandleMove(parts[1], parts[2], client) : "ERROR: Invalid move format";
                case "CHAT":
                    return HandleChat(message, client);
                case "RESTART":
                    return HandleRestart(client);
                case "GAMEOVER":
                    return HandleGameOver(client); 
                case "EXIT_ROOM":
                    return HandleExitRoom(client);
                case "LOGOUT":
                    return LogoutPlayer(client);
                case "EXIT_WAITING":
                    return HandleExitWaiting(client);
                default:
                    return "ERROR: Unknown command";
            }
        }
        private string HandleGameOver(TcpClient client)
        {
            return "GAMEOVER";
        }
        private string HandleExitWaiting(TcpClient client)
        {
            var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
            if (room == null)
                return "Error: Room not found.";
            room.RemovePlayer(client);
            if (room.IsEmpty())
            {
                gameRooms.Remove(room);
            }
            return "EXIT_WAITING";
        }
        private string HandleExitRoom(TcpClient client)
        {
            var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
            if (room == null)
                return "Error: Room not found.";
            room.RemovePlayer(client);
            if (room.IsEmpty())
            {
                gameRooms.Remove(room);
            }
            return $"EXITROOM";

        }
        private string LogoutPlayer(TcpClient client)
        {
            string username = activeSessions.FirstOrDefault(x => x.Value == client).Key;

            if (string.IsNullOrEmpty(username))
            {
                return "ERROR: User not logged in.";
            }
            bool removedFromActiveSessions = activeSessions.TryRemove(username, out _);
            if (removedFromActiveSessions)
            {
                Console.WriteLine($"User {username} has been removed from active sessions.");
            }
            lock (gameRooms)
            {
                var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
                if (room != null)
                {
                    room.RemovePlayer(client);
                    if (room.Player1 == null && room.Player2 == null)
                    {
                        gameRooms.Remove(room);
                        Console.WriteLine($"Game room {room.RoomId} has been removed.");
                    }
                }
            }

            Console.WriteLine($"User {username} has successfully logged out.");
            return "SUCCESS: Logged out.";
        }
        private string HandleRestart(TcpClient client)
        {
            var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
            if (room == null)
                return "Error: Room not found.";
            if (room.Player1 == null || room.Player2 == null)
            {
                return "ERROR: Not enough players to restart the game.";
            }
            room.SetPlayerWantsRestart(client, true);
            
            if (room.AllPlayersWantRestart())
            {
                room.ClearRestartFlags();
                SendMessage(room.Player1, "SUCCESS: restart game");
                SendMessage(room.Player2, "SUCCESS: restart game");
                Console.WriteLine($"Room {room.RoomId}: Game restarted by both players.");
                return "SUCCESS: Game restarted successfully.";
            }
            else
            {
                return "WAITING: Waiting for the other player to agree.";
            }
        }
        private string RegisterPlayer(string username, string password)
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "INSERT INTO players (username, password) VALUES (@username, @password)";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@username", username);
                    command.Parameters.AddWithValue("@password", password);
                    try
                    {
                        command.ExecuteNonQuery();
                        return "SUCCESS: Registered";
                    }
                    catch (SQLiteException ex)
                    {
                        // Lỗi khóa duy nhất (unique constraint)
                        if (ex.ResultCode == SQLiteErrorCode.Constraint)
                        {
                            return "ERROR: Username already exists";
                        }
                        return "ERROR: Database error: " + ex.Message;
                    }
                }
            }
        }

        private string LoginPlayer(string username, string password, TcpClient client)
        {
            if (activeSessions.ContainsKey(username))
            {
                Console.WriteLine($"User {username} is already logged in.");
                return "ERROR: Tài khoản đã được đăng nhập trên một thiết bị khác.";
            }
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string query = "SELECT COUNT(*) FROM players WHERE username = @username AND password = @password";
                using (var command = new SQLiteCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@username", username);
                    command.Parameters.AddWithValue("@password", password);
                    long count = (long)command.ExecuteScalar();
                    if (count > 0)
                    {
                        activeSessions[username] = client;
                        return "SUCCESS: logged in";
                    }
                    else return "ERROR: Error from log in!";
                }
            }
        }

        private string FindMatch(TcpClient client)
        {
            lock (waitingPlayers)
            {
                if (waitingPlayers.Contains(client))
                {
                    return "ERROR: Already in a match";
                }

                waitingPlayers.Enqueue(client);
                if (waitingPlayers.Count >= 2)
                {
                    TcpClient player1 = waitingPlayers.Dequeue();
                    TcpClient player2 = waitingPlayers.Dequeue();
                    GameRoom gameRoom = new GameRoom(player1, player2);
                    gameRooms.Add(gameRoom);
                    SendMessage(player1, "SUCCESS: white");
                    SendMessage(player2, "SUCCESS: black");

                    return "SUCCESS: Match started";
                }
                else
                {
                    return "WAITING: Finding match...";
                }
            }
        }

        private string CreateRoom(string roomId, TcpClient client)
        {
            // Kiểm tra nếu phòng đã tồn tại
            if (gameRooms.Any(g => g.RoomId == roomId))
            {
                return "ERROR: Room already exists";
            }

            // Tạo phòng mới và thêm vào danh sách
            GameRoom newRoom = new GameRoom(roomId, client);
            gameRooms.Add(newRoom);
            return $"ROOM_CREATED {roomId} created. Waiting for opponent.";
        }

        private string JoinRoom(string roomId, TcpClient client)
        {
            GameRoom room = gameRooms.FirstOrDefault(g => g.RoomId == roomId);
            if (room == null)
            {
                return "ERROR: Room not found";
            }

            // Kiểm tra nếu phòng đã đủ 2 người chơi
            if (room.IsFull())
            {
                return "ERROR: Room is full";
            }

            // Thêm người chơi vào phòng
            room.AddPlayer(client);
            SendMessage(room.Player1, "JOIN_ROOM: A player has joined the room");
            return $"SUCCESS: You have joined room {roomId}";
        }

        private string HandleMove(string from, string to, TcpClient client)
        {
            var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
            if (room == null)
            {
                return "ERROR: Not in a game room.";
            }

            if (!room.IsCurrentPlayer(client))
                return "ERROR: Not your turn";

            room.SendMove(from, to, client);
            return "SUCCESS: Move sent";
        }

        private string HandleChat(string message, TcpClient client)
        {
            var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
            if (room == null)
            {
                return "ERROR: Not in a game room.";
            }

            room.SendChatMessage(message, client);
            return "SUCCESS: Message sent";
        }

        private void RemovePlayer(TcpClient client)
        {
            lock (waitingPlayers)
            {
                if (waitingPlayers.Contains(client))
                {
                    waitingPlayers = new Queue<TcpClient>(waitingPlayers.Where(p => p != client));
                }

                var room = gameRooms.FirstOrDefault(g => g.HasPlayer(client));
                if (room != null)
                {
                    room.RemovePlayer(client);
                   // gameRooms.Remove(room);
                }
            }
            var username = activeSessions.FirstOrDefault(x => x.Value == client).Key;
            if (username != null)
            {
                activeSessions.TryRemove(username, out _);
                Console.WriteLine($"User {username} logged out.");
            }
        }

        private async void SendMessage(TcpClient client, string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        private void CreateDatabase()
        {
            using (var connection = new SQLiteConnection(connectionString))
            {
                connection.Open();
                string createTableQuery = @"
                    CREATE TABLE IF NOT EXISTS players (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT UNIQUE NOT NULL,
                        password TEXT NOT NULL
                    );";
                using (var command = new SQLiteCommand(createTableQuery, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    internal class GameRoom
    {
        public string RoomId { get; set; }
        public TcpClient Player1 { get;  set; }
        public TcpClient Player2 { get;set; }
        private int currentPlayerIndex = 0;

        public GameRoom(string roomId, TcpClient player1)
        {
            RoomId = roomId;
            Player1 = player1;
        }
        public GameRoom(TcpClient player1, TcpClient player2)
        {
            Player1 = player1;
            Player2 = player2;
        }
        public bool IsFull()
        {
            return Player2 != null;
        }

        public void AddPlayer(TcpClient client)
        {
            if (Player2 == null)
            {
                Player2 = client;
            }
        }

        public bool HasPlayer(TcpClient client)
        {
            return Player1 == client || Player2 == client;
        }

        public bool IsCurrentPlayer(TcpClient client)
        {
            return (currentPlayerIndex == 0 && Player1 == client) || (currentPlayerIndex == 1 && Player2 == client);
        }

        public void SendMove(string from, string to, TcpClient client)
        {
            TcpClient opponent = (client == Player1) ? Player2 : Player1;
            SendMessage(opponent, $"MOVE {from} {to}");
            UpdateCurrentPlayer();
        }

        private void UpdateCurrentPlayer()
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % 2;
        }

        private async void SendMessage(TcpClient client, string message)
        {
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                NetworkStream stream = client.GetStream();
                await stream.WriteAsync(data, 0, data.Length);
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        public void SendChatMessage(string message, TcpClient sender)
        {
            TcpClient recipient = sender == Player1 ? Player2 : Player1;
            SendMessage(recipient, $"CHAT {message}");
        }

        public void RemovePlayer(TcpClient client)
        {
            if (Player1 == client)
            {
                Player1 = null;
                Console.WriteLine("Removed client from game room");
            } else
            if (Player2 == client)
            {
                Player2 = null;
                Console.WriteLine("Removed client from game room");
            }
        }
        private Dictionary<TcpClient, bool> restartFlags = new Dictionary<TcpClient, bool>();

        public void SetPlayerWantsRestart(TcpClient client, bool wantsRestart)
        {
            if (restartFlags.ContainsKey(client))
                restartFlags[client] = wantsRestart;
            else
                restartFlags.Add(client, wantsRestart);
        }

        public bool AllPlayersWantRestart()
        {
            return restartFlags.ContainsKey(Player1) && restartFlags[Player1] && restartFlags.ContainsKey(Player2) && restartFlags[Player2];
        }

        public void ClearRestartFlags()
        {
            restartFlags.Clear();
        }
        public bool IsEmpty()
        {
            return Player1 == null && Player2 == null;
        }
    }
}
