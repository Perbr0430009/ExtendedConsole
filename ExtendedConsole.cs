using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace ExtendedConsole
{
    public class ExtendedConsole
    {
        #region P/invokes
        //Nécéssaire pour avoir accéder au buffer de la console
        [DllImport("Kernel32.dll", SetLastError = true)]
        static extern IntPtr GetStdHandle(uint nStdHandle);

        //Nécéssaire pour écrire dans le buffer de la console
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteConsoleOutput(
        SafeFileHandle hConsoleOutput,
        [MarshalAs(UnmanagedType.LPArray), In] CHAR_INFO[] lpBuffer,
        COORD dwBufferSize,
        COORD dwBufferCoord,
        ref RECT lpWriteRegion);
        #endregion

        private static CHAR_INFO[][,] Layers = new CHAR_INFO[3][,];

        private static int vCursorPosX = 0;
        private static int vCursorPosY = 0;
        private static int activeLayer = 0;

        public static ConsoleColor disabledColor { get; set; } = ConsoleColor.Gray;
        public static ConsoleColor selectionColor { get; set; } = ConsoleColor.DarkGreen;
        public static ConsoleColor lineUIColor { get; set; } = ConsoleColor.Blue;

        static ExtendedConsole()
        {
            Console.CursorVisible = false;
            //Initie chaque layers
            for (int i = 0; i < Layers.Length; i++)
                Layers[i] = new CHAR_INFO[Console.BufferWidth + 1, Console.WindowHeight + 1];
        }

        /// <summary>Change le nombre de Layers. Chaque layer rajoute un check par case lors de l'Update(). Évitez les layers inutiles pour optimizer.</summary>
        public static void SetNumberOfLayers(int numberOfLayers)
        {
            CHAR_INFO[][,] oldLayers = Layers;
            Layers = new CHAR_INFO[numberOfLayers][,];
            for (int i = 0; i < Layers.Length; i++)
            {
                if (i < oldLayers.Length)
                    Layers[i] = oldLayers[i];
                else
                    Layers[i] = new CHAR_INFO[Console.BufferWidth, Console.WindowHeight];
            }
        }

        /// <summary>Le curseur virtuel indique aux autres méthodes l'origine à utiliser. Aucune autre méthode ne déplace ce curseur.</summary>
        public static void SetVirtualCursorPosition(int _x, int _y)
        {
            if (_x >= 0 && _x < Layers[0].GetLength(0))
                vCursorPosX = _x;
            if (_y >= 0 && _y < Layers[0].GetLength(1))
                vCursorPosY = _y;
        }

        /// <summary>Change le layer actif, celui qui seras modifié par les autres méthodes</summary>
        public static void SetActiveLayer(int _activeLayer)
        {
            if (_activeLayer >= 0 && _activeLayer < Layers.Length)
                activeLayer = _activeLayer;
        }

        /// <summary>Écrit quelque chose sur le layer actif sans mettre à jour la console.</summary>
        public static void VirtualWrite(string text, int x = -1, int y = -1)
        {
            int nextCharX = x;
            int nextCharY = y;
            if (nextCharX == -1)
                nextCharX = vCursorPosX;
            if (nextCharY == -1)
                nextCharY = vCursorPosY;

            short currentAttributes = GetAttributesValue(Console.ForegroundColor, Console.BackgroundColor);
            byte[] eASCII = Encoding.GetEncoding(437).GetBytes(text);   //Converti le texte en un tableau de byte ASCII extended

            for (int i = 0; i < text.Length; i++)
            {
                //Prépare et écrit dans la prochaine case du layer les infos au standard win32
                CHAR_INFO ci = new CHAR_INFO();
                ci.charData = new byte[] { eASCII[i], (byte)text[i] };
                ci.attributes = currentAttributes;
                Layers[activeLayer][nextCharX, nextCharY] = ci;

                //Décide de la prochaine case où écrire, change de ligne au besoin
                nextCharX++;
                if (nextCharX >= Console.BufferWidth)
                {
                    nextCharX = 0;
                    nextCharY++;
                    if (nextCharY >= Layers[0].GetLength(1))
                        nextCharY = 0;
                }
            }
        }

        /// <summary>Efface une zone sur le layer actif aux coordonées du pointeur virtuel sans mettre à jour la console.</summary>
        public static void VirtualErase(int _width = -1, int _height = -1, int x = -1, int y = -1)
        {
            if (x == -1)
                x = vCursorPosX;
            if (y == -1)
                y = vCursorPosY;

            if (_width < 0 && _height < 0)
                VirtualLayerReset();    //Efface le layer au complet si valeurs par default sont utilisée
            else
            {
                //contraint la zone à effacer dans les limites du tableau.
                if (x + _width > Console.BufferWidth)
                    _width = Console.BufferWidth - x;
                if (y + _height > Console.WindowHeight)
                    _height = Console.WindowHeight - y;

                //Réinitialize les positions à effacer
                for (int i = 0; i < _width; i++)
                    for (int j = 0; j < _height; j++)
                        Layers[activeLayer][i + x, j + y] = new CHAR_INFO();
            }
        }

        /// <summary>Réinitialize un layers sans mettre à jour la console. Layer actif par default</summary>
        public static void VirtualLayerReset(int _layerIndex = -1)
        {
            int index = _layerIndex;
            if (_layerIndex < 0 || _layerIndex >= Layers.Length)
                index = activeLayer;

            Layers[index] = new CHAR_INFO[Console.BufferWidth, Console.WindowHeight];
        }

        /// <summary>Réinitialize tous les layers sans mettre à jour la console.</summary>
        public static void VirtualClear()
        {
            for (int i = 0; i < Layers.Length; i++)
                Layers[i] = new CHAR_INFO[Console.BufferWidth, Console.WindowHeight];
        }

        /// <summary>Update une zone de la console en compilant les données du systeme de layers. Update l'intégrale de la console par défault.</summary>
        public static void Update(int left = 0, int top = 0, int width = -1, int height = -1)
        {
            //Contraint les arguments dans la zone
            if (left < 0 || left > Console.BufferWidth)
                left = 0;
            if (top < 0 || top > Console.WindowHeight)
                top = 0;
            if (width < 0 || left + width > Layers[0].GetLength(0))
                width = Layers[0].GetLength(0) - left - 1;
            if (height < 0 || top + height > Layers[0].GetLength(1))
                height = Layers[0].GetLength(1) - top - 1;

            //Initie les elements nécéssaires pour communiquer avec l'API Windows
            RECT zoneAUpdater = new RECT(left, top, width, height);
            COORD origin = new COORD(left, top);
            COORD size = new COORD(zoneAUpdater.Right, zoneAUpdater.Bottom);

            //La commande win32 utilisé plus loin copy l'info des cases situé dans les limites d'un RECT (struct) d'un tableau de CHAR_INFO (struct) aux la même position dans le buffer de la console
            //crée un tableau en conséquence et le rempli uniquement des characters à afficher
            CHAR_INFO[] buf = new CHAR_INFO[(left + width) * (top + height)];   //window utilise un tableau à une dimention (optimization de bas niveau)
            for (int i = 0; i < left + width; i++)
                for (int j = 0; j < top + height; j++)
                    if (i >= left && j >= top)
                        buf[i + (j * (left + width))] = GetCHARINFOAtPosition(i, j);

            //envoi ça a window pour qu'il l'écrive dans le GPU
            IntPtr StdHandle = GetStdHandle(unchecked((uint)-11));
            SafeFileHandle handle = new SafeFileHandle(StdHandle, false);
            if (!WriteConsoleOutput(handle, buf, size, origin, ref zoneAUpdater))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            //handle.SetHandleAsInvalid();
        }

        /// <summary>Retourne la prochaine touche "valide" entrée par l'utilisateur</summary>
        public static ConsoleKeyInfo GetUserInput(ConsoleKey[] validInput = null, bool addConfirmInput = false, bool addCancelInput = false, bool addLetterInput = false)
        {
            ConsoleKeyInfo userInput = new ConsoleKeyInfo();

            if (validInput == null)     //pour éviter une erreur
                validInput = new ConsoleKey[0];

            if (addConfirmInput)
            {
                ConsoleKey[] _newValidInput = new ConsoleKey[validInput.Length + 3];

                for (int i = 0; i < validInput.Length; i++)
                    _newValidInput[i] = validInput[i];

                _newValidInput[validInput.Length] = ConsoleKey.Enter;
                _newValidInput[validInput.Length + 1] = ConsoleKey.Spacebar;
                _newValidInput[validInput.Length + 2] = ConsoleKey.NumPad5;
                validInput = _newValidInput;
            }

            if (addCancelInput)
            {
                ConsoleKey[] _newValidInput = new ConsoleKey[validInput.Length + 2];

                for (int i = 0; i < validInput.Length; i++)
                    _newValidInput[i] = validInput[i];

                _newValidInput[validInput.Length] = ConsoleKey.Escape;
                _newValidInput[validInput.Length + 1] = ConsoleKey.Backspace;
                validInput = _newValidInput;
            }

            // Vide le buffer de la Console. Les entrées sont stocker dans ce buffer avant d'être traitées.
            while (Console.KeyAvailable)
                Console.ReadKey(true);

            // En cas d'erreur
            if (validInput.Length == 0)
            {
                Console.Clear();
                Console.WriteLine("ERROR - ExtendedConsole.GetUserInput() WAS CALLED WITHOUT VALID INPUTS.");
                Console.ReadKey(true);
                Console.WriteLine("DEBUG MODE: Enter any input now.");
                return Console.ReadKey(true);
            }

            //Accepte seulement une entrée valide
            bool hasFinished = false;
            while (!hasFinished)
            {
                userInput = Console.ReadKey(true);
                foreach (ConsoleKey _keyCode in validInput)
                    if (userInput.Key == _keyCode || (char.IsLetter(userInput.KeyChar) && addLetterInput))
                        hasFinished = true;
            }
            return userInput;
        }

        /// <summary>Retourne la prochaine touche "valide" entrée par l'utilisateur</summary>
        public static ConsoleKeyInfo GetUserInput(bool addConfirmInput = false, bool addCancelInput = false, bool addLetterInput = false)
        {
            return GetUserInput(null, addConfirmInput, addCancelInput, addLetterInput);
        }

        /// <summary>Affiche une liste d'option et retourne l'index de celle choisi par l'utilisateur</summary>
        public static int ShowMenuAndGetChoice(string[] _options, int _width = -1, int _startingPosition = 1, bool _canCancel = true, bool[] _disabledOptions = null)
        {
            int width = 0;
            if (_width < 0)
                foreach (string s in _options)
                    if (width < s.Length)
                        width = s.Length;

            //S'assure que les entrées ne causesont pas d'erreur
            if (_disabledOptions == null)
                _disabledOptions = new bool[_options.Length];

            if (_startingPosition < 1 || _startingPosition > _options.Length)
                _startingPosition = 1;

            if (_disabledOptions.Length != _options.Length)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("ERROR: PASSED UNMATCHING ARRAYS SIZE TO ExtendedConsole.ShowMenuAndGetChoice()");
                Console.ReadKey();
                Console.WriteLine("Initializing default _disabledOptions[]");
                Console.ReadKey();
                _disabledOptions = new bool[_options.Length];
            }

            if (_options.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("ERROR(helper): ");
                Console.ReadKey();
                Console.WriteLine("Initializing default _disabledOptions[]");
                Console.ReadKey();
                _options = new string[] { "ERROR" };
            }

            //Processus de selection
            ConsoleColor currentBGColor = Console.BackgroundColor;
            ConsoleColor currentFGColor = Console.ForegroundColor;

            int originY = vCursorPosY;
            //Normalize la taille des options et écrit chacune d'elle à l'écran
            for (int i = 0; i < _options.Length; i++)
            {
                while (_options[i].Length < width)
                    _options[i] += " ";

                if (_disabledOptions[i])
                    Console.ForegroundColor = disabledColor;
                if (i + 1 == _startingPosition)
                    Console.BackgroundColor = selectionColor;
                vCursorPosY = originY + i;
                VirtualWrite(_options[i].Substring(0, width));

                Console.BackgroundColor = currentBGColor;
                Console.ForegroundColor = currentFGColor;

            }
            Update();
            vCursorPosY = originY;

            int choice = _startingPosition;
            ConsoleKeyInfo userInput;
            bool hasFinished = false;
            while (!hasFinished)
            {
                userInput = GetUserInput(new ConsoleKey[] { ConsoleKey.UpArrow, ConsoleKey.NumPad8, ConsoleKey.DownArrow, ConsoleKey.NumPad2 }, true, true);

                switch (userInput.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.NumPad8:
                        if (_options.Length == 1)
                            continue;

                        if (choice == 1)
                        {
                            Console.BackgroundColor = currentBGColor;
                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                            choice = _options.Length;
                            if (selectionColor == currentBGColor)
                                Console.BackgroundColor = ConsoleColor.DarkGreen;
                            else
                                Console.BackgroundColor = selectionColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                        }
                        else
                        {
                            Console.BackgroundColor = currentBGColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                            choice--;
                            if (selectionColor == currentBGColor)
                                Console.BackgroundColor = ConsoleColor.DarkGreen;
                            else
                                Console.BackgroundColor = selectionColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                        }

                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.NumPad2:
                        if (_options.Length == 1)
                            continue;

                        if (choice == _options.Length)
                        {
                            Console.BackgroundColor = currentBGColor;
                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                            choice = 1;
                            if (selectionColor == currentBGColor)
                                Console.BackgroundColor = ConsoleColor.DarkGreen;
                            else
                                Console.BackgroundColor = selectionColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                        }
                        else
                        {
                            Console.BackgroundColor = currentBGColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                            choice++;
                            if (selectionColor == currentBGColor)
                                Console.BackgroundColor = ConsoleColor.DarkGreen;
                            else
                                Console.BackgroundColor = selectionColor;

                            vCursorPosY = originY + choice - 1;
                            VirtualWrite(_options[choice - 1]);
                        }
                        break;
                    case ConsoleKey.Spacebar:
                    case ConsoleKey.Enter:
                    case ConsoleKey.NumPad5:
                        if (!_disabledOptions[choice - 1])
                            hasFinished = true;
                        break;
                    case ConsoleKey.Escape:
                    case ConsoleKey.Backspace:
                        choice = -1;
                        hasFinished = true;
                        break;
                }
                Update();
            }
            Console.BackgroundColor = currentBGColor;
            return choice - 1;
        }

        /// <summary>Ouverture animée d'une boite de menu sur le layer actif</summary>
        /// <param name="_openingSpeed">En milisecondes. Le résultat est aproximatif</param>
        public static void AnimatedMenuBoxOpening(int x, int y, int width, int height, int _openingSpeed = 500, string[] image = null, bool doubleLines = true)
        {
            ConsoleColor currentColor = Console.ForegroundColor;

            int sleepTimeHorizontal = (int)((float)_openingSpeed / width * ((float)width / (2f * height + width)));
            int sleepTimeVertical = (int)(_openingSpeed / (height / 2f) * ((float)height / (2f * height + width)));

            //Initialize l'image si null.
            if (image == null)
            {
                image = new string[height - 2];
                string emptyline = "";
                while (emptyline.Length < width - 2)
                    emptyline += " ";
                for (int i = 0; i < image.Length; i++)
                {
                    image[i] = emptyline;
                }
            }
            else //Redimentionne l'image aux dimentions spécifiée, si necessaire
            {
                if (image.Length < height - y)
                {
                    string[] _resizedImage = new string[height - y];
                    for (int i = 0; i < _resizedImage.Length; i++)
                    {
                        if (image.Length > i)
                            _resizedImage[i] = image[i];
                        else
                            _resizedImage[i] = "";
                    }
                    image = _resizedImage;

                    for (int i = 0; i < image.Length; i++)
                    {
                        if (image[i].Length > width)
                            image[i] = image[i].Substring(0, width);
                        else while (image[i].Length < width - 2)
                                image[i] += " ";
                    }
                }
            }

            //Aggrandi une ligne horizontale à partir du milieu
            int middlePos = y + (height / 2);
            Console.ForegroundColor = lineUIColor;
            for (int i = 0; i <= width / 2; i++)
            {
                int lineXStartPoint = x + (width / 2) - i;
                int lineLength = i * 2;
                if (lineLength > width)
                    lineLength--;
                if (doubleLines)
                {
                    VirtualWrite("═", lineXStartPoint, middlePos);
                    VirtualWrite("═", lineXStartPoint + lineLength - 1, middlePos);
                }
                else
                {
                    VirtualWrite("─", lineXStartPoint, middlePos);
                    VirtualWrite("─", lineXStartPoint + lineLength - 1, middlePos);
                }
                //Relie la boite au reste de l'UI
                VirtualLinkUILines(lineXStartPoint + 1, middlePos, doubleLines);
                VirtualLinkUILines(lineXStartPoint, middlePos, doubleLines);
                VirtualLinkUILines(lineXStartPoint + lineLength - 2, middlePos, doubleLines);
                VirtualLinkUILines(lineXStartPoint + lineLength - 1, middlePos, doubleLines);

                Update(x, y, width, height);
                Thread.Sleep(sleepTimeHorizontal);
            }

            //Ouvre la boite verticalement
            for (int i = 0; i < (height / 2); i++)
            {
                int lineYStartPoint = y + (height / 2) - i;

                Console.ForegroundColor = currentColor;
                int lineLength = i * 2;
                if (height % 2 == 1)
                    lineLength++;


                if (lineLength > height)
                    lineLength--;
                int lineToDraw = height / 2 - i - 1;
                VirtualWrite(image[lineToDraw], x + 1, lineYStartPoint);
                lineToDraw = height / 2 + i - 2;
                if (height % 2 == 1)
                    lineToDraw++;
                VirtualWrite(image[lineToDraw], x + 1, lineYStartPoint + lineLength - 1);

                Console.ForegroundColor = lineUIColor;
                if (doubleLines)
                {
                    VirtualWrite("║", x, lineYStartPoint);
                    VirtualWrite("║", x, lineYStartPoint + lineLength - 1);
                    VirtualWrite("║", x + width - 1, lineYStartPoint);
                    VirtualWrite("║", x + width - 1, lineYStartPoint + lineLength - 1);
                }
                else
                {
                    VirtualWrite("│", x, lineYStartPoint);
                    VirtualWrite("│", x, lineYStartPoint + lineLength - 1);
                    VirtualWrite("│", x + width - 1, lineYStartPoint);
                    VirtualWrite("│", x + width - 1, lineYStartPoint + lineLength - 1);
                }

                //Complete la boite et la relie au reste de l'UI
                VirtualDrawHorizontalLine(lineYStartPoint - 1, x, width, doubleLines);
                VirtualLinkUILines(x, lineYStartPoint, doubleLines);
                VirtualLinkUILines(x + width - 1, lineYStartPoint, doubleLines);
                VirtualDrawHorizontalLine(lineYStartPoint + lineLength, x, width, doubleLines);
                VirtualLinkUILines(x, lineYStartPoint + lineLength - 1, doubleLines);
                VirtualLinkUILines(x + width - 1, lineYStartPoint + lineLength - 1, doubleLines);
                for (int j = 1; j < width - 1; j++)
                {
                    bool aboveIsLinked = IsLinkedDown(x + j, lineYStartPoint - 2);
                    if (aboveIsLinked)
                        if (doubleLines)
                            VirtualWrite("╩", x + j, lineYStartPoint - 1);
                        else
                            VirtualWrite("┴", x + j, lineYStartPoint - 1);

                    bool belowIsLinked = IsLinkedDown(x + j, lineYStartPoint + lineLength + 1);
                    if (belowIsLinked)
                        if (doubleLines)
                            VirtualWrite("╦", x + j, lineYStartPoint + lineLength);
                        else
                            VirtualWrite("┬", x + j, lineYStartPoint + lineLength);
                }

                Thread.Sleep(sleepTimeVertical);
                Update(x, y, width, height);
            }
            Console.ForegroundColor = currentColor;
        }

        /// <summary>Ouverture animée d'une boite de menu sur le layer actif, adaptée a la taille de l'image</summary>
        /// <param name="_openingSpeed">Le résultat est aproximatif</param>
        public static void AnimatedMenuBoxOpening(int x, int y, string[] image, int _openingSpeed = 500)
        {
            int height = image.Length + 2;
            int width = 0;

            foreach (string s in image)
                if (width < s.Length + 2)
                    width = s.Length + 2;

            AnimatedMenuBoxOpening(x, y, width, height, _openingSpeed, image);
        }

        /// <summary>Animation de la fermeture d'une boite de menu sur le layer actif</summary>
        /// <param name="_closingSpeed">Le résultat est aproximatif</param>
        public static void AnimatedMenuBoxClosing(int x, int y, int width, int height, int closingSpeed = 500, bool doubleLines = true)
        {
            ConsoleColor currentColor = Console.ForegroundColor;
            Console.ForegroundColor = lineUIColor;
            int sleepTimeHorizontal = (int)((float)closingSpeed / width * ((float)width / (2 * height + width)));
            int sleepTimeVertical = (int)(closingSpeed / (height / 2f) * ((float)height / (2 * height + width)));

            //ferme Verticalement
            for (int i = (height / 2); i > 0; i--)
            {
                VirtualErase(width, 1, x, y + (height / 2) - i);
                VirtualErase(width, 1, x, y + (height / 2) + i);
                VirtualDrawHorizontalLine(y + (height / 2) - i + 1, x, width);
                VirtualDrawHorizontalLine(y + (height / 2) + i - 1, x, width);
                //Relie au reste de l'UI
                for (int j = 1; j < width - 1; j++)
                {
                    bool isLinkedUp = IsLinkedDown(x + j, y + (height / 2) - i);
                    bool isLinkedDown = IsLinkedUp(x + j, y + (height / 2) + i);
                    if (i != 1)
                    {
                        if (isLinkedUp)
                        {
                            if (doubleLines)
                                VirtualWrite("╩", x + j, (y + (height / 2) - i + 1));
                            else
                                VirtualWrite("┴", x + j, (y + (height / 2) - i + 1));
                        }

                        if (isLinkedDown && (height / 2) + i <= height)
                        {
                            if (doubleLines)
                                VirtualWrite("╦", x + j, (y + (height / 2) + i - 1));
                            else
                                VirtualWrite("┬", x + j, (y + (height / 2) + i - 1));
                        }
                    }
                    else
                    {
                        if (isLinkedUp && isLinkedDown)
                            if (doubleLines)
                                VirtualWrite("╬", x + j, (y + (height / 2) + i - 1));
                            else
                                VirtualWrite("┼", x + j, (y + (height / 2) + i - 1));
                        else if (isLinkedDown)
                            if (doubleLines)
                                VirtualWrite("╦", x + j, (y + (height / 2) + i - 1));
                            else
                                VirtualWrite("┬", x + j, (y + (height / 2) + i - 1));
                        else if (isLinkedUp)
                            if (doubleLines)
                                VirtualWrite("╩", x + j, (y + (height / 2) + i - 1));
                            else
                                VirtualWrite("┴", x + j, (y + (height / 2) + i - 1));
                    }

                }
                Update();
                Thread.Sleep(sleepTimeVertical);
            }

            //ferme horizontalement
            for (int i = width / 2; i >= 0; i--)
            {
                VirtualErase(1, 1, x + (width / 2) - i, y + (height / 2));
                VirtualErase(1, 1, x + (width / 2) + i, y + (height / 2));
                Update();
                Thread.Sleep(sleepTimeHorizontal);
            }
            Console.ForegroundColor = currentColor;
        }

        /// <summary>Animation de la fermeture d'une boite de menu sur le layer actif</summary>
        /// <param name="_closingSpeed">Le résultat est aproximatif</param>
        public static void AnimatedMenuBoxClosing(int x, int y, string[] image, int _openingSpeed = 500)
        {
            int height = image.Length + 2;
            int width = 0;

            foreach (string s in image)
                if (width < s.Length + 2)
                    width = s.Length + 2;

            AnimatedMenuBoxClosing(x, y, width, height, _openingSpeed);
        }

        /// <summary>Dessine une boite en ascii sur le layer actif sans mettre à jour la console</summary>
        public static void VirtualDrawBox(int left, int top, int width, int height, bool doubleLines = true)
        {
            if (width > 1 && height > 1)
            {
                VirtualDrawHorizontalLine(top, left, width, doubleLines);
                VirtualDrawHorizontalLine(top + height - 1, left, width, doubleLines);

                VirtualDrawVerticalLine(left, top, height, doubleLines);
                VirtualDrawVerticalLine(left + width - 1, top, height, doubleLines);

            }
            else if (width > 1)
                VirtualDrawHorizontalLine(top, left, width, doubleLines);
            else if (height > 1)
                VirtualDrawVerticalLine(left, top, height, doubleLines);
        }

        static bool IsLinkedDown(int x, int y)
        {
            bool isLinked = false;
            CHAR_INFO ci = GetCHARINFOAtPosition(x, y);
            switch (ci.charData[0])
            {
                case 186://║
                case 187://╗
                case 203://╦
                case 201://╔
                case 185://╣
                case 204://╠
                case 206://╬
                case 179://│
                case 191://┐
                case 194://┬
                case 218://┌
                case 180://┤
                case 195://├
                case 197://┼
                    isLinked = true;
                    break;
            }
            return isLinked;
        }

        static bool IsLinkedUp(int x, int y)
        {
            bool isLinked = false;
            CHAR_INFO ci = GetCHARINFOAtPosition(x, y);
            switch (ci.charData[0])
            {
                case 186://║
                case 188://╝
                case 202://╩
                case 200://╚
                case 185://╣
                case 204://╠
                case 206://╬
                case 179://│
                case 217://┘
                case 193://┴
                case 192://└
                case 180://┤
                case 195://├
                case 197://┼
                    isLinked = true;
                    break;
            }
            return isLinked;
        }

        static bool IsLinkedLeft(int x, int y)
        {
            bool isLinked = false;
            CHAR_INFO ci = GetCHARINFOAtPosition(x, y);
            switch (ci.charData[0])
            {
                case 205://═
                case 187://╗
                case 202://╩
                case 188://╝
                case 203://╦
                case 185://╣
                case 206://╬
                case 196://─
                case 191://┐
                case 193://┴
                case 217://┘
                case 194://┬
                case 180://┤
                case 197://┼
                    isLinked = true;
                    break;
            }
            return isLinked;
        }

        static bool IsLinkedRight(int x, int y)
        {
            bool isLinked = false;
            CHAR_INFO ci = GetCHARINFOAtPosition(x, y);
            switch (ci.charData[0])
            {
                case 205://═
                case 201://╔
                case 203://╦
                case 200://╚
                case 202://╩
                case 204://╠
                case 206://╬
                case 196://─
                case 192://└
                case 194://┬
                case 218://┌
                case 193://┴
                case 195://├
                case 197://┼
                    isLinked = true;
                    break;
            }
            return isLinked;
        }

        /// <summary>Remplace le symbole ASCII a la position par celui adéquat en fonction des lignes d'UI adjaceantes</summary>
        public static void VirtualLinkUILines(int x, int y, bool doubleLines = true)
        {
            int currentCursorPosX = vCursorPosX;
            int currentCursorPosY = vCursorPosY;
            vCursorPosX = x;
            vCursorPosY = y;

            bool aboveIsLinked = IsLinkedDown(x, y - 1);
            bool belowIsLinked = IsLinkedUp(x, y + 1);
            bool leftIsLinked = IsLinkedRight(x - 1, y);
            bool rightIsLinked = IsLinkedLeft(x + 1, y);

            if (aboveIsLinked)
            {
                if (rightIsLinked)
                {
                    if (belowIsLinked)
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╬");
                            else
                                VirtualWrite("┼");
                        }
                        else
                        {
                            if (doubleLines)
                                VirtualWrite("╠");
                            else
                                VirtualWrite("├");
                        }
                    }
                    else //below isn't linked
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╩");
                            else
                                VirtualWrite("┴");
                        }
                        else
                        {
                            if (doubleLines)
                                VirtualWrite("╚");
                            else
                                VirtualWrite("└");
                        }
                    }
                }
                else //right isn't linked
                {
                    if (belowIsLinked)
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╣");
                            else
                                VirtualWrite("┤");
                        }
                        else
                        {
                            if (doubleLines)
                                VirtualWrite("║");
                            else
                                VirtualWrite("│");
                        }
                    }
                    else //below isn't linked
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╝");
                            else
                                VirtualWrite("┘");
                        }
                    }
                }
            }
            else //above isn't linked
            {
                if (rightIsLinked)
                {
                    if (belowIsLinked)
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╦");
                            else
                                VirtualWrite("┬");
                        }
                        else
                        {
                            if (doubleLines)
                                VirtualWrite("╔");
                            else
                                VirtualWrite("┌");
                        }
                    }
                    else //below isn't linked
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("═");
                            else
                                VirtualWrite("─");
                        }
                    }
                }
                else //right isn't linked
                {
                    if (belowIsLinked)
                    {
                        if (leftIsLinked)
                        {
                            if (doubleLines)
                                VirtualWrite("╗");
                            else
                                VirtualWrite("┐");
                        }
                    }
                }
            }

            vCursorPosX = currentCursorPosX;
            vCursorPosY = currentCursorPosY;
        }

        /// <summary>Desinne un ligne horizontale ASCII sur le layer actif sans updater la console</summary>
        public static void VirtualDrawHorizontalLine(int y, int start, int width, bool doubleLines = true)
        {
            int currentCursorPosX = vCursorPosX;
            int currentCursorPosY = vCursorPosY;
            ConsoleColor currentColor = Console.ForegroundColor;
            Console.ForegroundColor = lineUIColor;

            for (int i = 0; i < width; i++)
            {
                CHAR_INFO ci = GetCHARINFOAtPosition(start + i, y);

                SetVirtualCursorPosition(start + i, y);

                switch (ci.charData[0])
                {
                    case 187: //╗
                    case 201: //╔
                    case 203: //╦
                    case 191: //┐
                    case 218: //┌
                    case 194: //┬
                        if (doubleLines)
                            VirtualWrite("╦");
                        else
                            VirtualWrite("┬");
                        break;

                    case 188: //╝
                    case 200: //╚
                    case 202: //╩
                    case 217: //┘
                    case 192: //└
                    case 193: //┴
                        if (doubleLines)
                            VirtualWrite("╩");
                        else
                            VirtualWrite("┴");
                        break;

                    case 186: //║
                    case 179: //│
                        bool aboveIsLinked = IsLinkedDown(start + i, y - 1);
                        bool belowIsLinked = IsLinkedUp(start + i, y + 1);

                        if (aboveIsLinked)
                        {
                            if (belowIsLinked)
                            {
                                if (doubleLines)
                                    VirtualWrite("╬");
                                else
                                    VirtualWrite("┼");
                            }
                            else
                            {
                                if (doubleLines)
                                    VirtualWrite("╩");
                                else
                                    VirtualWrite("┴");
                            }
                        }
                        else
                        {
                            if (belowIsLinked)
                            {
                                if (doubleLines)
                                    VirtualWrite("╦");
                                else
                                    VirtualWrite("┬");
                            }
                            else
                            {
                                if (doubleLines)
                                    VirtualWrite("═");
                                else
                                    VirtualWrite("─");
                            }
                        }
                        break;

                    case 204://╠
                    case 185://╣
                    case 206://╬
                    case 195://├
                    case 180://┤
                    case 197://┼
                        if (doubleLines)
                            VirtualWrite("╬");
                        else
                            VirtualWrite("┼");
                        break;

                    default:
                        if (doubleLines)
                            VirtualWrite("═");
                        else
                            VirtualWrite("─");
                        break;
                }
            }

            VirtualLinkUILines(start, y, doubleLines);
            VirtualLinkUILines(start + width - 1, y, doubleLines);
            SetVirtualCursorPosition(currentCursorPosX, currentCursorPosY);
            Console.ForegroundColor = currentColor;
        }

        /// <summary>Desinne un ligne verticale ASCII sur le layer actif sans updater la console</summary>
        public static void VirtualDrawVerticalLine(int x, int start, int height, bool doubleLines = true)
        {
            int currentCursorPosX = vCursorPosX;
            int currentCursorPosY = vCursorPosY;
            ConsoleColor currentColor = Console.ForegroundColor;
            Console.ForegroundColor = lineUIColor;

            for (int i = 0; i < height; i++)
            {
                CHAR_INFO ci = GetCHARINFOAtPosition(x, start + i);

                SetVirtualCursorPosition(x, start + i);

                switch (ci.charData[0])
                {
                    case 187: //╗
                    case 188: //╝
                    case 185: //╣
                    case 191: //┐
                    case 217: //┘
                    case 180: //┤
                        if (doubleLines)
                            VirtualWrite("╣");
                        else
                            VirtualWrite("┤");
                        break;

                    case 201: //╔
                    case 200: //╚
                    case 204: //╠
                    case 218: //┌
                    case 192: //└
                    case 195: //├
                        if (doubleLines)
                            VirtualWrite("╠");
                        else
                            VirtualWrite("├");
                        break;

                    case 205: //═
                    case 196: //─
                        bool leftIsLinked = IsLinkedRight(x - 1, start + i);
                        bool rightIsLinked = IsLinkedLeft(x + 1, start + i);

                        if (leftIsLinked)
                        {
                            if (rightIsLinked)
                            {
                                if (doubleLines)
                                    VirtualWrite("╬");
                                else
                                    VirtualWrite("┼");
                            }
                            else
                            {
                                if (doubleLines)
                                    VirtualWrite("╣");
                                else
                                    VirtualWrite("┤");
                            }
                        }
                        else
                        {
                            if (rightIsLinked)
                            {
                                if (doubleLines)
                                    VirtualWrite("╠");
                                else
                                    VirtualWrite("├");
                            }
                            else
                            {
                                if (doubleLines)
                                    VirtualWrite("║");
                                else
                                    VirtualWrite("│");
                            }
                        }
                        break;

                    case 203://╦
                    case 202://╩
                    case 206://╬
                    case 194://┬
                    case 193://┴
                    case 197://┼
                        if (doubleLines)
                            VirtualWrite("╬");
                        else
                            VirtualWrite("┼");
                        break;

                    default:
                        if (doubleLines)
                            VirtualWrite("║");
                        else
                            VirtualWrite("│");
                        break;
                }
            }

            VirtualLinkUILines(x, start);
            VirtualLinkUILines(x, start + height - 1);

            SetVirtualCursorPosition(currentCursorPosX, currentCursorPosY);
            Console.ForegroundColor = currentColor;
        }

        /// <returns>la struct CHAR_INFO qui seras visible après la prochaine update</returns>
        static CHAR_INFO GetCHARINFOAtPosition(int _x, int _y)
        {
            CHAR_INFO toReturn = new CHAR_INFO();

            if (_x >= 0 && _x <= Layers[0].GetLength(0) && _y >= 0 && _y < Layers[0].GetLength(1))
                for (int i = Layers.Length - 1; i >= 0; i--)
                    if (Layers[i][_x, _y].charData != null)
                    {
                        toReturn.charData = Layers[i][_x, _y].charData;
                        toReturn.attributes = Layers[i][_x, _y].attributes;
                        return toReturn;
                    }

            toReturn.charData = new byte[] { 0, 0 };
            toReturn.attributes = 0;
            return toReturn;
        }

        #region Trucs pour WIN32
        /// <summary>Combine 2 couleurs en une valeur compatible avec le format utiliser par le buffer de la console</summary>
        static short GetAttributesValue(ConsoleColor _foregroundColor, ConsoleColor _backgroundColor)
        {
            short color = 0;
            color += (short)_foregroundColor;
            color += (short)(16 * (int)_backgroundColor);
            return color;
        }

        //Structures nécessaires pour communiquer avec win32
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;

            public RECT(int _x, int _y, int width, int height)
            {
                Left = (short)_x;
                Top = (short)_y;
                Right = (short)(_x + width);
                Bottom = (short)(_y + height);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;

            public COORD(int _x, int _y) : this()
            {
                X = (short)_x;
                Y = (short)_y;
            }

            public COORD(short _x, short _y) : this()
            {
                X = _x;
                Y = _y;
            }

            public void Coord(int _x, int _y)
            {
                X = (short)_x;
                Y = (short)_y;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CHAR_INFO
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] charData;
            public short attributes;
        }

        #endregion
    }

}

/*
 ╔ = 201;
 ╚ = 200;
 ╗ = 187;
 ╝ = 188;
 ╠ = 204;
 ║ = 186;
 ╣ = 185;
 ╩ = 202;
 ═ = 205;
 ╦ = 203;
 ╬ = 206;
 ┌ = 218;
 └ = 192;
 ┐ = 191;
 ┘ = 217;
 ├ = 195;
 │ = 179;
 ┤ = 180;
 ┴ = 193;
 ─ = 196;
 ┬ = 194;
 ┼ = 197;
*/
