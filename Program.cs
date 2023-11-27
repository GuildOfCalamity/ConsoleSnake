#region [Includes]
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#endregion [Includes]
/*
   ╦══════════════════════════════════════════╦
   ║  Console Snake (Snake Jr.) by Manaconda  ║
   ║  Copyright © 2022, All rights reserved.  ║ 
   ╩══════════════════════════════════════════╩
*/
namespace Snake
{
    internal enum Direction
    {
        Down, Left, Right, Up
    }

    class Program
    {
        public static Func<Exception, string> FormatException = new Func<Exception, string>(ex =>
        {
            string result = "";
            result += "Error Source....: " + ex.Source;
            result += "\nError Message...: " + ex.Message;
            result += "\nStack Trace.....: " + ex.StackTrace;
            result += "\nInnerEx(Source).: " + ex.InnerException?.Source;
            result += "\nInnerEx(Message): " + ex.InnerException?.Message;
            result += "\nInnerEx(Trace)..: " + ex.InnerException?.StackTrace;
            return result;
        });

        #region [Global Scope Variables]
        // NOTE: Lots-o-globals here. These could be relegated to other classes to govern certain aspects of game-play.
        const int SPEED = 200, SHORZ = 60, SVERT = 80; // creation constants
        static string _bodyChar = "▓";                 // ░ ▒ ▓
        static string _headChar = "░";                 // ░ ▒ ▓
        static string _foodChar = "▒";                 // ░ ▒ ▓
        static object _lockObj = new object();         // for thread syncing
        static bool _useKeyThread = false;             // whether to use a seperate thread for keyboard signals (in beta, use at your own risk)
        static bool _drawing = false;                  // indicator of current drawing state
        static bool _running = true;                   // main game loop flag
        static bool _special = false;                  // magic food flag (concept needs to be re-worked)
        static int _score = 0;                         // total point score
        static int _snakeLen = 3;                      // initial snake length
        static int _speed = SPEED;                     // starting speed
        static int _rateHorz = SHORZ;                  // horizontal speed (should be ~25% faster than vertical)
        static int _rateVert = SVERT;                  // vertical speed (glyphs are taller than they are wide, however some terminal fonts are almost perfect squares)
        static int _loopRate = 0;                      // used by metric timer
        static int _tmrFreq = 2;                       // update frequency (in seconds)
        static int _termWidth = 60;                    // Size(X) WARNING: This value can be problematic since it's based on the total terminal buffer size.
        static int _termHeight = 24;                   // Size(Y) WARNING: This value can be problematic since it's based on the total terminal buffer size.
        static Timer _tmrState = null;                 // timer for metrics
        static Settings settings = null;               // for config file settings (currently font name & size)
        static IntPtr _consoleHandle = IntPtr.Zero;    // pointer to ourselves
        static Direction _direction = Direction.Right; // starting direction
        #endregion [Global Scope Variables]

        #region [Properties]
        public static IntPtr ConsoleHandle
        {
            get
            {
                if (_consoleHandle == IntPtr.Zero)
                {
                    try { _consoleHandle = ConsoleHelper.GetConsoleWindow(); }
                    catch (Exception ex) { Debug.WriteLine($"ConsoleHandle: {ex.Message}"); }
                }
                return _consoleHandle;
            }
        }
        #endregion [Properties]

        #region [Core System]
        /// <summary>
        /// Entry point.
        /// </summary>
        static void Main()
        {
            // Keep watch for any errant wrong-doing.
            AppDomain.CurrentDomain.UnhandledException += new System.UnhandledExceptionEventHandler(CurrentDomain_UnhandledException);

            // Keep screen from locking due to OS idle flag.
            // Shouldn't be needed but it's here in case
            // user is AFK for extended period of time.
            PreventLock(); 

            // Use this in tandem with the console font setting.
            Size screenSize = new Size(_termWidth, _termHeight);

            // Load the app settings.
            settings = LoadSettings("Settings.cfg");

            #region [Move console to desired desktop location]
            // NOTE: Finally got this working and it was not trivial, go
            // ahead and search the web for a complete solution...I dare you.
            ConsoleHelper.RECT rect = new ConsoleHelper.RECT();
            ConsoleHelper.GetWindowRect(ConsoleHandle, out rect);
            ConsoleHelper.MoveWindow(ConsoleHandle, 200, 40, rect.Right - rect.Left, rect.Bottom - rect.Top, true);
            #endregion [Move console to desired desktop location]

            // For experimenting with different features, e.g. life-time score bonus
            _tmrState = new Timer(new TimerCallback(CheckState), null, TimeSpan.FromSeconds(_tmrFreq), TimeSpan.FromSeconds(_tmrFreq));

            Console.CursorVisible = false;

            while (_running)
            {
                FocusConsole();

                // Start a new game
                StartGame(screenSize);
                
                Console.ResetColor();
                if (_running)
                {
                    _speed = SPEED;    // reset starting speed
                    _rateHorz = SHORZ; // reset horizontal speed
                    _rateVert = SVERT; // reset vertical speed

                    Console.SetCursorPosition((screenSize.Width / 2) - 4, screenSize.Height / 2);
                    Console.Write("Game Over");
                    
                    // Console.Beep() causes the current thread to freeze for the
                    // duration of the sound, so fire it in the ThreadPool...
                    Task.Run(() => { Console.Beep(); });
                }
                else
                {
                    Console.SetCursorPosition((screenSize.Width / 2) - 5, screenSize.Height / 2);
                    Console.Write("Good Bye ☺");

                    _tmrState?.Dispose();
                }

                Thread.Sleep(2500);
            }
            Console.CursorVisible = true;
        }

        /// <summary>
        /// Setup the initial screen for game play.
        /// </summary>
        /// <param name="screenSize"><see cref="Size"/></param>
        static void StartGame(Size screenSize)
        {
            #region [Initial Setup]
            _score = 0;
            _snakeLen = 3;
            _direction = Direction.Right;
            Point foodLocation = Point.Empty;
            Queue<Point> snake = new Queue<Point>();
            Point currentPosition = new Point(0, 10); //start left-most and middle height
            snake.Enqueue(currentPosition);
            DrawScreen(screenSize);
            ShowScore(_score);

            if (_useKeyThread)
            {
                ThreadPool.QueueUserWorkItem((object o) =>
                {
                    // Any thread created using ThreadPool will be a background thread by default,
                    // so we will not have to worry about joining this on application exit.
                    while (_running)
                    {
                        // This update is latent. It would probably be better to set this up
                        // as an event based system and listen for Direction triggers to happen.
                        _direction = GetDirection(_direction);
                        Debug.WriteLine($"DIRECTION: {_direction}");
                    }
                });
            }
            #endregion [Initial Setup]

            while (UpdateSnake(snake, currentPosition, _snakeLen, screenSize) && _running)
            {
                // We could change this loop to utilize an actual framerate model, but Thread.Sleep() is so easy.
                Thread.Sleep(_speed);

                // Make sure we have the focus, if not just loop until we do.
                IntPtr test = ConsoleHelper.GetForegroundWindow();
                if (test == ConsoleHandle)
                {
                    if (!_useKeyThread)
                        _direction = GetDirection(_direction);
                    
                    // Determine where we'll be on the next update.
                    currentPosition = GetNextPosition(_direction, currentPosition);

                    if (currentPosition.Equals(foodLocation))
                    {
                        foodLocation = Point.Empty;
                        
                        if (_special) // check for magic food
                        {
                            // This is a horrible way to do this...
                            // The foodLocation object should really be
                            // changed to a struct so each position can
                            // hold more than just X,Y coord data.
                            _special = false;
                            _snakeLen += 6; 
                        }
                        else
                            _snakeLen += 3; // make snake a little bit longer

                        _score += (10 + _snakeLen);
                        ShowScore(_score); // force an update to the score
                        
                        // auto-adjust the difficulty
                        _rateHorz += 1;
                        _rateVert += 1;
                    }

                    if (foodLocation == Point.Empty)
                        foodLocation = ShowFood(screenSize, snake);
                }
                _loopRate++;
            }
        }

        /// <summary>
        /// This is called from our main loop to update the snake's coords and draw.
        /// </summary>
        static bool UpdateSnake(Queue<Point> snake, Point targetPosition, int snakeLength, Size screenSize)
        {
            // For a Queue, Last() is similar to Peek().
            Point lastPoint = snake.Last();

            if (lastPoint.Equals(targetPosition))
                return true;

            // Have we hit ourself?
            if (snake.Any(x => x.Equals(targetPosition)))
                return false;

            // Have we gone out of bounds?
            if (targetPosition.X < 0 || 
                targetPosition.X >= screenSize.Width || 
                targetPosition.Y < 0 || 
                targetPosition.Y >= screenSize.Height)
            {
                return false;
            }

            DrawCall(() => 
            {
                // draw tail
                Console.BackgroundColor = ConsoleColor.DarkGreen;
                Console.SetCursorPosition(lastPoint.X + 1, lastPoint.Y + 1);
                Console.Write(_bodyChar);

                // The next position we will be
                snake.Enqueue(targetPosition);

                // draw head
                Console.BackgroundColor = ConsoleColor.Red;
                Console.SetCursorPosition(targetPosition.X + 1, targetPosition.Y + 1);
                Console.Write(_headChar);

                // remove old tail
                if (snake.Count > snakeLength)
                {
                    Point removePoint = snake.Dequeue();
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.SetCursorPosition(removePoint.X + 1, removePoint.Y + 1);
                    Console.Write(" ");
                }
            });
            return true;
        }

        /// <summary>
        /// Find a good spot to lay down some grub for Snake.
        /// </summary>
        static Point ShowFood(Size screenSize, Queue<Point> snake)
        {
            int edgeSize = 1;
            int diceRoll = Util.Rnd.Next(11);
            Point foodPoint = Point.Empty;
            Point snakeHead = snake.Last();
            
            do
            {   // Find an appropriate spot to place the food...
                int x = Util.Rnd.Next(0, screenSize.Width - edgeSize);
                int y = Util.Rnd.Next(0, screenSize.Height - edgeSize);
                
                if (snake.All(p => p.X != x || p.Y != y) && (Math.Abs(x - snakeHead.X) + Math.Abs(y - snakeHead.Y)) > 8)
                    foodPoint = new Point(x, y);

            } while (foodPoint == Point.Empty);

            DrawCall(() =>
            {
                // NOTE: Our snake body will be green, so don't draw a green food
                // point as it may be hard to see when the snake body is very long.
                if (diceRoll >= 8)
                    Console.BackgroundColor = ConsoleColor.Blue;
                else if (diceRoll >= 6)
                    Console.BackgroundColor = ConsoleColor.DarkYellow;
                else if (diceRoll >= 4)
                    Console.BackgroundColor = ConsoleColor.DarkMagenta;
                else if (diceRoll >= 2)
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                else
                {
                    _special = true;
                    Console.BackgroundColor = ConsoleColor.White;
                }

                Console.SetCursorPosition(foodPoint.X + 1, foodPoint.Y + 1);
                Console.Write(_foodChar);
            });

            return foodPoint;
        }

        /// <summary>
        /// For drawing top banner text.
        /// </summary>
        static void ShowScore(int score)
        {
            ThreadPool.QueueUserWorkItem((object o) =>
            {
                while (_drawing) 
                { 
                    Debug.WriteLine($"[WAITING_FOR_DRAWCALL_TO_FINISH]"); 
                    Thread.Yield(); // Relinquish control to other threads until drawing is finished.
                }

                lock (_lockObj)
                {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.SetCursorPosition(1, 0);
                    if (_termWidth >= 70)
                        Console.Write("Score: {0,-10} {1,16} {2,35}", score.ToString("000000"), "Len: " + _snakeLen, "Snake Jr. ☺ 2022");
                    else if (_termWidth >= 50)
                        Console.Write("Score: {0,-10} {1,13} {2,28}", score.ToString("000000"), "Len: " + _snakeLen, "Snake Jr. ☺ 2022");
                    else // minimum spacing
                        Console.Write("Score: {0,-10} {1,8} {2,16}", score.ToString("000000"), "Len: " + _snakeLen, "Snake Jr. ☺ 2022");
                }
            });
        }

        /// <summary>
        /// A simple title animation sample.
        /// </summary>
        static void ShowTitle(string msg)
        {
            for (int i = _termWidth / 2 - (msg.Length / 2); i > 1; i--)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.SetCursorPosition(i, 11);
                Console.Write(msg);
                Thread.Sleep(60);
                Console.SetCursorPosition(i, 11);
                Console.Write(new string(' ', msg.Length));
            }
        }

        /// <summary>
        /// Draw the inital game bounds (only called once per game).
        /// You can use Console.WindowTop and Console.WindowWidth of the 
        /// System.Console class to set the location of the console window.
        /// The BufferHeight and BufferWidth property gets/sets the number
        /// of rows and columns to be displayed.
        /// WindowHeight and WindowWidth properties must always be less
        /// than BufferHeight and BufferWidth respectively.
        /// WindowLeft must be less than BufferWidth minus WindowWidth and
        /// WindowTop must be less than BufferHeight minus WindowHeight.
        /// WindowLeft and WindowTop are relative to the buffer allocation.
        /// </summary>
        /// <param name="size">The desired screen <see cref="Size"/></param>
        static void DrawScreen(Size size)
        {
            Console.Title = "Snake Jr.";

            DrawCall(() =>
            {
                Console.WindowHeight = size.Height + 2;
                Console.WindowWidth = size.Width + 2;
                Console.BufferHeight = Console.WindowHeight;
                Console.BufferWidth = Console.WindowWidth;
                Console.CursorVisible = false;
                Console.BackgroundColor = ConsoleColor.Gray;
                //Console.SetWindowSize(Console.WindowWidth, Console.WindowHeight);
                //Console.SetBufferSize(Console.WindowWidth, Console.WindowHeight);
                //Console.SetWindowPosition(0, 0);
                Console.Clear();
                Console.BackgroundColor = ConsoleColor.Black;
                
                // Initialize the playing field...
                for (int row = 0; row < size.Height; row++)
                {
                    //Thread.Sleep(1);
                    for (int col = 0; col < size.Width; col++)
                    {
                        Console.SetCursorPosition(col + 1, row + 1);
                        Console.Write(" ");
                    }
                }

                //ShowTitle("Get Ready!"); // <-- this needs work

                // Do we have a settings config file?
                if (settings != null) {
                    try {
                        // It's best to do this after initializing the Window Sizes and Buffers.
                        ConsoleHelper.SetCurrentFont(settings.FontName, Convert.ToInt16(settings.FontSize));
                    }
                    catch (Exception) {
                        // It's best to do this after initializing the Window Sizes and Buffers.
                        ConsoleHelper.SetCurrentFont("Consolas", 42);
                    }
                }
                else {
                    // It's best to do this after initializing the Window Sizes and Buffers.
                    ConsoleHelper.SetCurrentFont("Consolas", 42);
                }
            });
        }

        /// <summary>
        /// Calculate direction based on current/previous keyboard input.
        /// </summary>
        /// <returns><see cref="Direction"/></returns>
        static Direction GetDirection(Direction currentDirection)
        {
            if (!_useKeyThread)
            {
                // NOTE: When GetDirection() is on it's own thread, there's no
                // need to have thread blocking check of Console.KeyAvailable.
                if (!Console.KeyAvailable)
                    return currentDirection;
            }

            ConsoleKey key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.DownArrow:
                case ConsoleKey.S:
                    _speed = _rateVert;
                    if (currentDirection != Direction.Up)
                        currentDirection = Direction.Down;
                    break;
                case ConsoleKey.UpArrow:
                case ConsoleKey.W:
                    _speed = _rateVert;
                    if (currentDirection != Direction.Down)
                        currentDirection = Direction.Up;
                    break;
                case ConsoleKey.LeftArrow:
                case ConsoleKey.A:
                    _speed = _rateHorz;
                    if (currentDirection != Direction.Right)
                        currentDirection = Direction.Left;
                    break;
                case ConsoleKey.RightArrow:
                case ConsoleKey.D:
                    _speed = _rateHorz;
                    if (currentDirection != Direction.Left)
                        currentDirection = Direction.Right;
                    break;
                case ConsoleKey.OemPlus:
                case ConsoleKey.Add:
                    if (_rateHorz > 20) { _rateHorz -= 10; }
                    if (_rateVert > 20) { _rateVert -= 10; }
                    if (currentDirection == Direction.Up || currentDirection == Direction.Down) { _speed = _rateVert; }
                    else { _speed = _rateHorz; }
                    Debug.WriteLine($"Speed Increase: {_speed}");
                    break;
                case ConsoleKey.OemMinus:
                case ConsoleKey.Subtract:
                    if (_rateHorz < 500) { _rateHorz += 10; }
                    if (_rateVert < 500) { _rateVert += 10; }
                    if (currentDirection == Direction.Up || currentDirection == Direction.Down) { _speed = _rateVert; }
                    else { _speed = _rateHorz; }
                    Debug.WriteLine($"Speed Decrease: {_speed}");
                    break;
                case ConsoleKey.Escape:
                    _running = false;
                    break;
            }
            return currentDirection;
        }

        /// <summary>
        /// Calculate where we're about to be.
        /// </summary>
        /// <returns><see cref="Point"/></returns>
        static Point GetNextPosition(Direction direction, Point currentPosition)
        {
            Point nextPosition = new Point(currentPosition.X, currentPosition.Y);
            switch (direction)
            {
                case Direction.Up:
                    nextPosition.Y--;
                    break;
                case Direction.Left:
                    nextPosition.X--;
                    break;
                case Direction.Down:
                    nextPosition.Y++;
                    break;
                case Direction.Right:
                    nextPosition.X++;
                    break;
            }
            return nextPosition;
        }

        /// <summary>
        /// Not used, for testing only.
        /// </summary>
        static Point GetPrevPosition(Direction direction, Point currentPosition)
        {
            Point nextPosition = new Point(currentPosition.X, currentPosition.Y);
            switch (direction)
            {
                case Direction.Up:
                    nextPosition.Y++;
                    break;
                case Direction.Left:
                    nextPosition.X++;
                    break;
                case Direction.Down:
                    nextPosition.Y--;
                    break;
                case Direction.Right:
                    nextPosition.X--;
                    break;
            }
            return nextPosition;
        }


        /// <summary>
        /// A "macro" for thread locking and updating the _drawing bool flag.
        /// </summary>
        /// <param name="action">code to execute</param>
        static void DrawCall(Action action)
        {
            lock (_lockObj)
            {
                _drawing = true;
                action();
                _drawing = false;
            }
        }

        /// <summary>
        /// Callback for our <see cref="Program._tmrState"/> timer.
        /// </summary>
        static void CheckState(object state)
        {
            Debug.WriteLine($"[Loops/Sec: {_loopRate / _tmrFreq}]");
            
            _loopRate = 0;

            if (!_drawing)
                ShowScore(_score);
            else
                _score += 2; // award some points just for staying alive
        }

        /// <summary>
        /// Helper to bring console to foreground, will handle minimized state also.
        /// </summary>
        static void FocusConsole()
        {
            if (ConsoleHandle != IntPtr.Zero)
            {
                ConsoleHelper.ShowWindow(ConsoleHandle, ConsoleHelper.SW_RESTORE);
                Thread.Sleep(1);
                ConsoleHelper.SetForegroundWindow(ConsoleHandle);
            }
        }

        /// <summary>
        /// Prevent Idle-to-Lock (monitor not affected)
        /// </summary>
        static void PreventLock()
        {
            try
            {
                ConsoleHelper.SetThreadExecutionState(ConsoleHelper.EXECUTION_STATE.ES_DISPLAY_REQUIRED | ConsoleHelper.EXECUTION_STATE.ES_CONTINUOUS);
                //Console.WriteLine("ThreadExecutionState has been set to DISPLAY_REQUIRED | CONTINUOUS");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PreventLock: {ex.Message}");
            }
        }

        /// <summary>
        /// Prevent Idle-to-Sleep (monitor not affected)
        /// </summary>
        static void PreventSleep()
        {
            try
            {
                ConsoleHelper.SetThreadExecutionState(ConsoleHelper.EXECUTION_STATE.ES_CONTINUOUS | ConsoleHelper.EXECUTION_STATE.ES_AWAYMODE_REQUIRED);
                //Console.WriteLine("ThreadExecutionState has been set to AWAYMODE_REQUIRED | CONTINUOUS");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PreventLock: {ex.Message}");
            }
        }

        /// <summary>
        /// Domain exception handler.
        /// </summary>
        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.CursorVisible = true;
            Console.WriteLine(">>> UnhandledException event on " + DateTime.Now.ToLongDateString() + " at " + DateTime.Now.ToLongTimeString() + " <<<");
            Console.WriteLine(FormatException(e.ExceptionObject as Exception));
            Thread.Sleep(10000);
        }
        #endregion [Core System]

        #region [Config File Helpers]
        /// <summary>
        /// Load system settings from disk.
        /// </summary>
        /// <param name="filePath">name of config file</param>
        /// <returns><see cref="Settings"/> object</returns>
        static Settings LoadSettings(string fileName)
        {
            try
            {
                if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), fileName)))
                {
                    string imported = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), fileName)));
                    Debug.WriteLine($"> Config loaded: {imported}");
                    return FromJsonTo<Settings>(imported);
                }
                else
                {
                    Debug.WriteLine($"> No serial config was found, creating default config...");
                    Settings freshSettings = new Settings("Consolas", "32");
                    SaveSettings(freshSettings, "Settings.cfg");
                    return freshSettings;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"> LoadSettings(ERROR): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save system settings to disk.
        /// </summary>
        /// <param name="sysSettings"><see cref="SerialConfig"/> object</param>
        /// <param name="filePath">name of config file</param>
        /// <returns>true is successful, false otherwise</returns>
        static bool SaveSettings(Settings sysSettings, string filePath)
        {
            try
            {
                File.WriteAllBytes(System.IO.Path.Combine(Directory.GetCurrentDirectory(), filePath), Encoding.UTF8.GetBytes(ToJson(sysSettings)));
                Debug.WriteLine($"> Settings saved.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"> SaveSettings(ERROR): {ex.Message}");
                return false;
            }
        }


        /// <summary>
        /// Converts an object into a JSON string.
        /// </summary>
        /// <param name="item"></param>
        public static string ToJson(object item)
        {
            var ser = new DataContractJsonSerializer(item.GetType());
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, item);
                var sb = new StringBuilder();
                sb.Append(Encoding.UTF8.GetString(ms.ToArray()));
                return sb.ToString();
            }
        }

        /// <summary>
        /// Converts an IEnumerable collection into a JSON string.
        /// </summary>
        /// <param name="item"></param>
        public static string ToJson(IEnumerable collection, string rootName)
        {
            var ser = new DataContractJsonSerializer(collection.GetType());
            using (var ms = new MemoryStream())
            {
                ser.WriteObject(ms, collection);
                var sb = new StringBuilder();
                sb.Append("{ \"").Append(rootName).Append("\": ");
                sb.Append(Encoding.UTF8.GetString(ms.ToArray()));
                sb.Append(" }");
                return sb.ToString();
            }
        }

        /// <summary>
        /// Converts a JSON string into the specified type.
        /// </summary>
        /// <typeparam name="T">the requested type</typeparam>
        /// <param name="jsonString">the JSON data</param>
        /// <returns>the requested type</returns>
        public static T FromJsonTo<T>(string jsonString)
        {
            DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonString)))
            {
                T jsonObject = (T)ser.ReadObject(ms);
                return jsonObject;
            }
        }
        #endregion [Config File Helpers]
    }

    #region [Support Classes]
    /// <summary>
    /// A long running garbage-friendly reference so we don't 
    /// have to keep instantiating new Random() objects inside
    /// loops or other frequently used method calls.
    /// </summary>
    public static class Util
    {
        private static readonly WeakReference s_random = new WeakReference(null);
        public static Random Rnd {
            get {
                var r = (Random)s_random.Target;
                if (r == null)
                    s_random.Target = r = new Random();

                return r;
            }
        }
    }
    #endregion [Support Classes]
}
