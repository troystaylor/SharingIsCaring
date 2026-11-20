using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 1: CONNECTOR ENTRY POINT                                          ║
// ║                                                                            ║
// ║  MCP Apps games that an agent shows in chat: Snake, 2048, Minesweeper,     ║
// ║  Tetris, Cyber Trivia, Word Guess, Sudoku, Breakout, and Solitaire. Each   ║
// ║  game is a ui:// resource served by this script; the play_* tools link to  ║
// ║  a game through _meta.ui.resourceUri and return the game's settings in     ║
// ║  structuredContent.                                                        ║
// ║                                                                            ║
// ║  RegisterGameViews  - the ui:// resources                                  ║
// ║  RegisterGameTools  - the tools that open each game                        ║
// ║  GameViews          - the HTML, CSS, and JavaScript for each game          ║
// ║  TriviaBank         - the vetted Cyber Trivia questions                    ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

public class Script : ScriptBase
{
    // ── Server Configuration ─────────────────────────────────────────────

    /// <summary>
    /// Application Insights connection string (leave empty to disable telemetry).
    /// Format: InstrumentationKey=YOUR-KEY;IngestionEndpoint=https://REGION.in.applicationinsights.azure.com/;...
    /// </summary>
    private const string APP_INSIGHTS_CONNECTION_STRING = "";

    private static readonly McpServerOptions Options = new McpServerOptions
    {
        ServerInfo = new McpServerInfo
        {
            Name = "power-mcp-games",
            Version = "1.0.0",
            Title = "Power MCP Games",
            Description = "Snake, 2048, Minesweeper, Tetris, Cyber Trivia, Word Guess, Sudoku, Breakout, and Solitaire for agents, delivered as MCP Apps"
        },

        // Preferred revision. Legacy clients (Copilot Studio) that open with
        // initialize are detected and served under legacy semantics automatically.
        ProtocolVersion = "2026-07-28",
        SupportedProtocolVersions = new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" },

        Capabilities = new McpCapabilities
        {
            Tools = true,
            Resources = true,
            // Declares MCP Apps support so a host that negotiates the extension can see it.
            Extensions = new JObject
            {
                [McpKeys.ExtensionUi] = new JObject { ["mimeTypes"] = new JArray(McpKeys.UiMimeType) }
            }
            // Logging is deprecated as of 2026-07-28; leave off for new servers.
        },

        // Cache hints attached to list and read results (2026-07-28 CacheableResult).
        ListCacheTtlMs = 300000,      // 5 minutes
        ListCacheScope = "public",
        ResourceCacheTtlMs = 60000,   // 1 minute
        ResourceCacheScope = "public", // Game HTML is identical for every caller
        DiscoverCacheTtlMs = 3600000, // 1 hour

        Instructions =
            "Use play_snake, play_2048, play_minesweeper, play_tetris, play_cyber_trivia, play_word_guess, play_sudoku, play_breakout, or play_solitaire " +
            "to show a game the user can play in the conversation. " +
            "The game is shown to the user, so reply in one short sentence and do not explain the rules at length. " +
            "If the user says they cannot see the game, tell them this host cannot display it and offer something else; " +
            "do not try to play the game in text. " +
            "When the user finishes a game, their score is added to the conversation, so you can congratulate them or offer another round. " +
            "Never reveal or hint at the answers to Cyber Trivia or the hidden word in Word Guess."
    };

    // ── Entry Point ──────────────────────────────────────────────────────

    public override async Task<HttpResponseMessage> ExecuteAsync()
    {
        var correlationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        // 1. Create the handler
        var handler = new McpRequestHandler(Options);

        // 2. Register tools and resources
        RegisterCapabilities(handler);

        // 3. Wire up logging (optional)
        handler.OnLog = (eventName, data) =>
        {
            this.Context.Logger.LogInformation($"[{correlationId}] {eventName}");
            _ = LogToAppInsights(eventName, data, correlationId);
        };

        // 4. Handle the request — one line does everything
        var body = await this.Context.Request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var result = await handler.HandleAsync(
            body,
            McpTransportHeaders.FromRequest(this.Context.Request),
            this.CancellationToken).ConfigureAwait(false);

        var duration = DateTime.UtcNow - startTime;
        this.Context.Logger.LogInformation($"[{correlationId}] Completed in {duration.TotalMilliseconds}ms");

        // 5. Return the response
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(result, Encoding.UTF8, "application/json")
        };
    }

    // ── Capability Registration ─────────────────────────────────────────

    private void RegisterCapabilities(McpRequestHandler handler)
    {
        RegisterGameViews(handler);
        RegisterGameTools(handler);
    }

    // ── Game Views ─────────────────────────────────────────────────────

    private const string SnakeUri = "ui://power-mcp-games/snake";
    private const string Game2048Uri = "ui://power-mcp-games/2048";
    private const string MinesweeperUri = "ui://power-mcp-games/minesweeper";
    private const string TetrisUri = "ui://power-mcp-games/tetris";
    private const string CyberTriviaUri = "ui://power-mcp-games/cyber-trivia";
    private const string WordGuessUri = "ui://power-mcp-games/word-guess";
    private const string SudokuUri = "ui://power-mcp-games/sudoku";
    private const string BreakoutUri = "ui://power-mcp-games/breakout";
    private const string SolitaireUri = "ui://power-mcp-games/solitaire";

    /// <summary>
    /// Register the ui:// resources. No csp is declared, so the host applies its restrictive
    /// default: no network access and no external scripts. The games draw on a canvas or with
    /// plain elements and load nothing from the network.
    /// </summary>
    private void RegisterGameViews(McpRequestHandler handler)
    {
        handler.AddUiResource(SnakeUri, "Snake",
            "Classic Snake: steer the snake to eat food without hitting a wall or yourself.",
            GameViews.Snake, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(Game2048Uri, "2048",
            "Slide tiles so matching numbers combine. Reach 2048.",
            GameViews.Game2048, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(MinesweeperUri, "Minesweeper",
            "Reveal every safe cell without hitting a mine.",
            GameViews.Minesweeper, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(TetrisUri, "Tetris",
            "Classic falling blocks: clear lines to score.",
            GameViews.Tetris, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(CyberTriviaUri, "Cyber Trivia",
            "A security awareness quiz for company end users: scenario questions with instant feedback and a results screen.",
            GameViews.CyberTrivia, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(WordGuessUri, "Word Guess",
            "Guess the hidden five-letter word. Each guess shows which letters are right.",
            GameViews.WordGuess, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(SudokuUri, "Sudoku",
            "Fill the 9 by 9 grid so every row, column, and box holds the numbers 1 to 9.",
            GameViews.Sudoku, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(BreakoutUri, "Breakout",
            "Bounce the ball off the paddle to break every brick.",
            GameViews.Breakout, ui => ui["prefersBorder"] = false);

        handler.AddUiResource(SolitaireUri, "Solitaire",
            "Klondike Solitaire: build the four foundation piles from ace to king.",
            GameViews.Solitaire, ui => ui["prefersBorder"] = false);
    }

    // ── Game Tools ─────────────────────────────────────────────────────
    //
    //    Each play_* tool opens a game. It returns a plain-text note in content for the
    //    model and for hosts that cannot render MCP Apps, and the game's settings in
    //    structuredContent for the game to read. Tool arguments are clamped here, so the
    //    schema stays flat and free of numeric bounds.

    private void RegisterGameTools(McpRequestHandler handler)
    {
        handler.AddTool("play_snake",
            "Play the classic Snake game. The user steers the snake with the arrow keys, swipes, or on-screen buttons, " +
            "eats food to grow, and loses by hitting a wall or the snake itself.",
            schema: s => s
                .String("speed", "Game speed. Defaults to normal.", enumValues: new[] { "slow", "normal", "fast" }),
            handler: (args, ct) =>
            {
                var speed = GetChoice(args, "speed", "normal", "slow", "normal", "fast");
                var interval = speed == "slow" ? 200 : speed == "fast" ? 80 : 120;
                var data = new JObject { ["game"] = "snake", ["speed"] = speed, ["interval"] = interval };
                return Task.FromResult(GameResult(GameText("Snake", " at " + speed + " speed"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Snake",
            uiResourceUri: SnakeUri);

        handler.AddTool("play_2048",
            "Play the 2048 puzzle game. The user slides tiles with the arrow keys, swipes, or on-screen buttons; " +
            "matching numbers combine, and the goal is to reach 2048.",
            schema: s => s
                .Integer("gridSize", "Grid size from 3 to 6. Defaults to 4."),
            handler: (args, ct) =>
            {
                var size = GetInt(args, "gridSize", 4, 3, 6);
                var data = new JObject { ["game"] = "2048", ["gridSize"] = size };
                return Task.FromResult(GameResult(GameText("2048", " on a " + size + " by " + size + " grid"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play 2048",
            uiResourceUri: Game2048Uri);

        handler.AddTool("play_minesweeper",
            "Play Minesweeper. The user reveals cells and flags mines, and wins by revealing every safe cell.",
            schema: s => s
                .String("difficulty", "Difficulty. Easy is 9 by 9 with 10 mines, medium is 16 by 16 with 40, hard is 30 by 16 with 99. Defaults to easy.",
                    enumValues: new[] { "easy", "medium", "hard" }),
            handler: (args, ct) =>
            {
                var difficulty = GetChoice(args, "difficulty", "easy", "easy", "medium", "hard");
                var width = difficulty == "hard" ? 30 : difficulty == "medium" ? 16 : 9;
                var height = difficulty == "easy" ? 9 : 16;
                var mines = difficulty == "hard" ? 99 : difficulty == "medium" ? 40 : 10;
                var data = new JObject
                {
                    ["game"] = "minesweeper",
                    ["difficulty"] = difficulty,
                    ["width"] = width,
                    ["height"] = height,
                    ["mines"] = mines
                };
                return Task.FromResult(GameResult(GameText("Minesweeper", " on " + difficulty + " (" + mines + " mines)"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Minesweeper",
            uiResourceUri: MinesweeperUri);

        handler.AddTool("play_tetris",
            "Play Tetris. The user moves and rotates falling blocks with the arrow keys or on-screen buttons and clears lines to score.",
            schema: s => s
                .Integer("level", "Starting level from 1 to 20. A higher level is faster. Defaults to 1."),
            handler: (args, ct) =>
            {
                var level = GetInt(args, "level", 1, 1, 20);
                var data = new JObject { ["game"] = "tetris", ["level"] = level };
                return Task.FromResult(GameResult(GameText("Tetris", " starting at level " + level), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Tetris",
            uiResourceUri: TetrisUri);

        handler.AddTool("play_cyber_trivia",
            "Start Cyber Trivia, a security awareness quiz for company employees. The questions come from a vetted built-in bank " +
            "(phishing, passwords and MFA, social engineering, safe browsing, data handling, devices, remote work, using AI safely, " +
            "physical security, and reporting incidents). It plays as a game show with a host named Pat Ching, a money clock, and a double-value final question. " +
            "The user answers in the card and sees the explanation there. " +
            "Do not reveal, hint at, or discuss any answers, and do not write your own security guidance for the quiz. " +
            "Nothing is stored, so a result is not a training record.",
            schema: s => s
                .String("category", "Topic to quiz on. Defaults to all, which mixes every topic. Use company to play only the custom questions.",
                    enumValues: new[] { "all", "phishing", "passwords", "social-engineering", "safe-browsing", "data-handling", "devices", "remote-work", "ai-use", "physical", "reporting", "company" })
                .Integer("count", "Number of questions from 5 to 20. Defaults to 10.")
                .String("level", "Difficulty. Defaults to any.", enumValues: new[] { "any", "basic", "intermediate" })
                .String("companyName", "The company name to use in the questions. Defaults to your company.")
                .String("reportTo", "Who to report problems to, such as the security team or a mailbox. Defaults to your security team.")
                .Boolean("timed", "When true, each question has a 20-second money clock. When false, there is no clock. Defaults to true.")
                .String("tone", "Host style. Cheeky adds a snarky game show host; plain turns the host off. Defaults to cheeky.", enumValues: new[] { "cheeky", "plain" })
                .String("customQuestions", "Optional JSON text with company-specific questions, up to 20. A JSON array of objects, each with " +
                    "stem (text), correct (text), wrong (an array of 1 to 3 wrong answers), explain (text), and action (text)."),
            handler: (args, ct) =>
            {
                var data = BuildTrivia(args);
                data["timed"] = !(args?["timed"]?.Type == JTokenType.Boolean && !args["timed"].Value<bool>());
                data["tone"] = GetChoice(args, "tone", "cheeky", "cheeky", "plain");
                var detail = " with " + data["count"] + " questions";
                return Task.FromResult(GameResult(GameText("Cyber Trivia", detail) + " Do not reveal or hint at any answers.", data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Cyber Trivia",
            uiResourceUri: CyberTriviaUri);

        handler.AddTool("play_word_guess",
            "Play Word Guess, a five-letter word game. The user types or taps guesses, and each guess shows which letters are in the right spot, " +
            "in the word but elsewhere, or not in the word. By default the card picks a random word that you never see, so do not guess or reveal it. " +
            "Set word only when you want a themed round with a word you chose.",
            schema: s => s
                .String("word", "Optional five-letter word for the user to find, for a themed round. Leave empty for a random word.")
                .Integer("maxGuesses", "Number of guesses from 4 to 8. Defaults to 6.")
                .Boolean("hardMode", "When true, every guess must use the hints already revealed. Defaults to false.")
                .Boolean("highContrast", "When true, uses orange and blue instead of green and amber. Defaults to false."),
            handler: (args, ct) =>
            {
                var guesses = GetInt(args, "maxGuesses", 6, 4, 8);
                var hard = args?["hardMode"]?.Type == JTokenType.Boolean && args["hardMode"].Value<bool>();
                var contrast = args?["highContrast"]?.Type == JTokenType.Boolean && args["highContrast"].Value<bool>();
                var word = (args?["word"]?.ToString() ?? "").Trim().ToLowerInvariant();
                var data = new JObject { ["game"] = "word-guess", ["maxGuesses"] = guesses, ["hardMode"] = hard, ["highContrast"] = contrast };
                if (Regex.IsMatch(word, "^[a-z]{5}$")) data["word"] = word;
                var detail = " with " + guesses + " guesses" + (hard ? " in hard mode" : "");
                return Task.FromResult(GameResult(GameText("Word Guess", detail) + " Do not guess or reveal the word.", data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Word Guess",
            uiResourceUri: WordGuessUri);

        handler.AddTool("play_sudoku",
            "Play Sudoku. The user fills a 9 by 9 grid so every row, column, and box holds the numbers 1 to 9. " +
            "The card makes a new puzzle with one solution, and has pencil notes, undo, and hints. " +
            "Set puzzle only when you want a specific puzzle; do not solve the puzzle for the user unless they ask.",
            schema: s => s
                .String("difficulty", "Difficulty. Defaults to medium.", enumValues: new[] { "easy", "medium", "hard", "expert" })
                .String("puzzle", "Optional puzzle as 81 characters, row by row, using digits 1 to 9 for given numbers and 0 or a period for blanks.")
                .Integer("seed", "Optional whole number. The same difficulty and seed always make the same puzzle, so a game can be replayed."),
            handler: (args, ct) =>
            {
                var difficulty = GetChoice(args, "difficulty", "medium", "easy", "medium", "hard", "expert");
                var data = new JObject { ["game"] = "sudoku", ["difficulty"] = difficulty };
                var puzzle = Regex.Replace(args?["puzzle"]?.ToString() ?? "", @"\s+", "");
                if (Regex.IsMatch(puzzle, "^[0-9.]{81}$")) data["puzzle"] = puzzle;
                if (args?["seed"] != null) data["seed"] = GetInt(args, "seed", 0, 0, int.MaxValue);
                var detail = data["puzzle"] != null ? " with the puzzle you gave" : " on " + difficulty;
                return Task.FromResult(GameResult(GameText("Sudoku", detail), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Sudoku",
            uiResourceUri: SudokuUri);

        handler.AddTool("play_breakout",
            "Play Breakout. The user moves a paddle by dragging over the board, with the arrow keys, or with the on-screen buttons, " +
            "bounces a ball to break the bricks, and loses a life when the ball drops past the paddle.",
            schema: s => s
                .Integer("level", "Starting wall layout from 1 to 5. Defaults to 1.")
                .Integer("lives", "Number of lives from 1 to 5. Defaults to 3.")
                .String("speed", "Ball speed. Defaults to normal.", enumValues: new[] { "slow", "normal", "fast" }),
            handler: (args, ct) =>
            {
                var level = GetInt(args, "level", 1, 1, 5);
                var lives = GetInt(args, "lives", 3, 1, 5);
                var speed = GetChoice(args, "speed", "normal", "slow", "normal", "fast");
                var data = new JObject { ["game"] = "breakout", ["level"] = level, ["lives"] = lives, ["speed"] = speed };
                return Task.FromResult(GameResult(GameText("Breakout", " at " + speed + " speed with " + lives + " lives"), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Breakout",
            uiResourceUri: BreakoutUri);

        handler.AddTool("play_solitaire",
            "Play Klondike Solitaire in a classic Windows style. The user taps a card and then where it should go, double-taps a card to send it " +
            "to a foundation pile, and wins by building all four piles from ace to king.",
            schema: s => s
                .Integer("drawCount", "Cards turned over from the stock at a time: 1 or 3. Defaults to 1.")
                .Integer("deal", "Optional whole number. The same deal number always gives the same shuffle, so a game can be replayed.")
                .Integer("cardBack", "Card back design from 1 to 8: weave, robot, fish, reef, shell, castle, beach, or cards in hand. Defaults to 1. The player can change it in the game."),
            handler: (args, ct) =>
            {
                var draw = GetInt(args, "drawCount", 1, 1, 3) == 3 ? 3 : 1;
                var data = new JObject { ["game"] = "solitaire", ["drawCount"] = draw, ["cardBack"] = GetInt(args, "cardBack", 1, 1, 8) };
                if (args?["deal"] != null) data["deal"] = GetInt(args, "deal", 1, 1, int.MaxValue);
                return Task.FromResult(GameResult(GameText("Solitaire", " with draw " + draw), data));
            },
            annotations: a => { a["readOnlyHint"] = true; a["openWorldHint"] = false; },
            title: "Play Solitaire",
            uiResourceUri: SolitaireUri);
    }

    // ── Cyber Trivia ────────────────────────────────────────────────────

    /// <summary>
    /// Pick the questions for one quiz. Selection happens here, so the card receives only the chosen
    /// questions: filter, balance across topics, shuffle, then shuffle each question's answers.
    /// </summary>
    private static JObject BuildTrivia(JObject args)
    {
        var allowed = new List<string> { "all", "company" };
        allowed.AddRange(TriviaBank.Categories);
        var category = GetChoice(args, "category", "all", allowed.ToArray());
        var level = GetChoice(args, "level", "any", "any", "basic", "intermediate");
        var count = GetInt(args, "count", 10, 5, 20);
        var company = CleanText(args?["companyName"]?.ToString(), 60, "your company");
        var reportTo = CleanText(args?["reportTo"]?.ToString(), 80, "your security team");
        var rng = new Random();

        var custom = ParseCustomQuestions(args?["customQuestions"]?.ToString());
        if (category == "company" && custom.Count == 0) category = "all";

        var chosen = new List<TriviaQuestion>();
        if (category == "all" || category == "company")
        {
            Shuffle(custom, rng);
            chosen.AddRange(custom.Take(count));
        }
        if (category != "company" && chosen.Count < count)
        {
            var bank = TriviaBank.All.Where(q => category == "all" || q.Category == category).ToList();
            var preferred = bank.Where(q => level == "any" || q.Level == level).ToList();
            var rest = bank.Where(q => !preferred.Contains(q)).ToList();
            chosen.AddRange(PickBalanced(preferred, count - chosen.Count, rng));
            if (chosen.Count < count) chosen.AddRange(PickBalanced(rest, count - chosen.Count, rng));
        }
        Shuffle(chosen, rng);

        var questions = new JArray();
        for (var n = 0; n < chosen.Count; n++)
        {
            var q = chosen[n];
            var options = new List<string> { q.Correct };
            options.AddRange(q.Wrong);
            var order = Enumerable.Range(0, options.Count).ToList();
            Shuffle(order, rng);
            var shown = new JArray();
            var correct = 0;
            for (var i = 0; i < order.Count; i++)
            {
                shown.Add(FillTrivia(options[order[i]], company, reportTo));
                if (order[i] == 0) correct = i;
            }
            questions.Add(new JObject
            {
                ["id"] = n + 1,
                ["category"] = q.Category,
                ["categoryName"] = TriviaBank.CategoryName(q.Category),
                ["level"] = q.Level,
                ["stem"] = FillTrivia(q.Stem, company, reportTo),
                ["options"] = shown,
                ["correct"] = correct,
                ["explain"] = FillTrivia(q.Explain, company, reportTo),
                ["action"] = FillTrivia(q.Action, company, reportTo)
            });
        }

        return new JObject
        {
            ["game"] = "cyber-trivia",
            ["bankVersion"] = TriviaBank.Version,
            ["category"] = category,
            ["level"] = level,
            ["count"] = questions.Count,
            ["customCount"] = chosen.Count(q => q.Category == "company"),
            ["questions"] = questions
        };
    }

    /// <summary>Take up to n questions, rotating through the topics so a mixed quiz is balanced.</summary>
    private static List<TriviaQuestion> PickBalanced(List<TriviaQuestion> pool, int n, Random rng)
    {
        var groups = pool.GroupBy(q => q.Category).Select(g => g.ToList()).ToList();
        foreach (var g in groups) Shuffle(g, rng);
        Shuffle(groups, rng);
        var picked = new List<TriviaQuestion>();
        for (var round = 0; picked.Count < n; round++)
        {
            var added = false;
            foreach (var g in groups)
            {
                if (round < g.Count && picked.Count < n) { picked.Add(g[round]); added = true; }
            }
            if (!added) break;
        }
        return picked;
    }

    private static void Shuffle<T>(IList<T> list, Random rng)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            var t = list[i]; list[i] = list[j]; list[j] = t;
        }
    }

    /// <summary>Trim a free-text argument to a safe length. The card shows it through textContent, so this only limits size.</summary>
    private static string CleanText(string value, int max, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = Regex.Replace(value.Trim(), @"\s+", " ");
        return value.Length > max ? value.Substring(0, max).TrimEnd() : value;
    }

    /// <summary>Fill the {company} and {reportTo} placeholders, then start the text with a capital letter.</summary>
    private static string FillTrivia(string value, string company, string reportTo)
    {
        if (string.IsNullOrEmpty(value)) return "";
        value = value.Replace("{company}", company).Replace("{reportTo}", reportTo);
        return char.ToUpperInvariant(value[0]) + value.Substring(1);
    }

    /// <summary>Read the optional customQuestions argument. Anything that does not fit the shape is skipped.</summary>
    private static List<TriviaQuestion> ParseCustomQuestions(string json)
    {
        var list = new List<TriviaQuestion>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        JArray items;
        try { items = JArray.Parse(json); }
        catch (Exception) { return list; }

        foreach (var item in items.OfType<JObject>().Take(20))
        {
            var stem = CleanText(item["stem"]?.ToString(), 250, null);
            var correct = CleanText(item["correct"]?.ToString(), 150, null);
            var wrong = (item["wrong"] as JArray ?? new JArray())
                .Select(w => CleanText(w?.ToString(), 150, null))
                .Where(w => w != null).Take(3).ToList();
            if (stem == null || correct == null || wrong.Count == 0) continue;
            if (wrong.Contains(correct) || wrong.Distinct().Count() != wrong.Count) continue;
            list.Add(new TriviaQuestion
            {
                Category = "company",
                Level = "custom",
                Stem = stem,
                Correct = correct,
                Wrong = wrong.ToArray(),
                Explain = CleanText(item["explain"]?.ToString(), 300, ""),
                Action = CleanText(item["action"]?.ToString(), 200, "")
            });
        }
        return list;
    }

    // ── Game Helpers ────────────────────────────────────────────────────

    /// <summary>Tool result with a text note for the model and plain-text hosts, and the game's settings for the view.</summary>
    private static JObject GameResult(string text, JObject data)
    {
        return McpRequestHandler.ToolResult(new JArray { McpRequestHandler.TextContent(text) }, data);
    }

    private static string GameText(string name, string detail)
    {
        return name + " is shown to the user" + detail + ". Keep your reply to one short sentence and do not explain the rules at length. " +
            "If the user says they cannot see the game, tell them this host cannot display it and offer something else; do not try to play it in text.";
    }

    /// <summary>Read a whole-number argument, falling back to a default and clamping to a range.</summary>
    private static int GetInt(JObject args, string name, int defaultValue, int min, int max)
    {
        double parsed;
        try { parsed = args?[name] == null ? defaultValue : args[name].Value<double>(); }
        catch (Exception) { parsed = defaultValue; }
        return Math.Max(min, Math.Min(max, (int)Math.Round(parsed)));
    }

    /// <summary>Read a string argument that must be one of the allowed values, falling back to a default.</summary>
    private static string GetChoice(JObject args, string name, string defaultValue, params string[] allowed)
    {
        var raw = args?[name]?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        raw = raw.Trim().ToLowerInvariant();
        return Array.IndexOf(allowed, raw) >= 0 ? raw : defaultValue;
    }

    // ── Application Insights (Optional) ──────────────────────────────────

    private async Task LogToAppInsights(string eventName, object properties, string correlationId)
    {
        try
        {
            var instrumentationKey = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "InstrumentationKey");
            var ingestionEndpoint = ExtractConnectionStringPart(APP_INSIGHTS_CONNECTION_STRING, "IngestionEndpoint")
                ?? "https://dc.services.visualstudio.com/";

            if (string.IsNullOrEmpty(instrumentationKey))
                return;

            var propsDict = new Dictionary<string, string>
            {
                ["ServerName"] = Options.ServerInfo.Name,
                ["ServerVersion"] = Options.ServerInfo.Version,
                ["CorrelationId"] = correlationId
            };

            if (properties != null)
            {
                var propsJson = JsonConvert.SerializeObject(properties);
                var propsObj = JObject.Parse(propsJson);
                foreach (var prop in propsObj.Properties())
                {
                    propsDict[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            var telemetryData = new
            {
                name = $"Microsoft.ApplicationInsights.{instrumentationKey}.Event",
                time = DateTime.UtcNow.ToString("o"),
                iKey = instrumentationKey,
                data = new
                {
                    baseType = "EventData",
                    baseData = new
                    {
                        ver = 2,
                        name = eventName,
                        properties = propsDict
                    }
                }
            };

            var json = JsonConvert.SerializeObject(telemetryData);
            var telemetryUrl = new Uri(ingestionEndpoint.TrimEnd('/') + "/v2/track");

            var telemetryRequest = new HttpRequestMessage(HttpMethod.Post, telemetryUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            await this.Context.SendAsync(telemetryRequest, this.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Suppress telemetry errors
        }
    }

    private static string ExtractConnectionStringPart(string connectionString, string key)
    {
        if (string.IsNullOrEmpty(connectionString)) return null;
        var prefix = key + "=";
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return part.Substring(prefix.Length);
        }
        return null;
    }
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 2: MCP FRAMEWORK                                                  ║
// ║                                                                            ║
// ║  Built-in McpRequestHandler that brings MCP C# SDK patterns to Power       ║
// ║  Platform. If Microsoft enables the official SDK namespaces, this section   ║
// ║  becomes a using statement instead of inline code.                          ║
// ║                                                                            ║
// ║  Spec coverage: MCP 2026-07-28, dual-era                                   ║
// ║                                                                            ║
// ║  2026-07-28 removed the initialize handshake and made MCP stateless: every ║
// ║  request carries its own protocol version, identity, and capabilities in   ║
// ║  _meta. Power Platform connectors are stateless by construction, so the    ║
// ║  runtime and the protocol now agree rather than fight.                     ║
// ║                                                                            ║
// ║  This handler serves BOTH eras from one endpoint:                          ║
// ║    modern (2026-07-28)  server/discover, per-request _meta, resultType,    ║
// ║                         ttlMs/cacheScope, MRTR input_required              ║
// ║    legacy (2025-11-25-) initialize, ping, logging/setLevel,                ║
// ║                         resources/subscribe                                ║
// ║                                                                            ║
// ║  The era is chosen per request. Copilot Studio is a legacy client and is   ║
// ║  answered exactly as it was before this revision.                          ║
// ║                                                                            ║
// ║  Out of reach for a request/response connector (no open stream):           ║
// ║   - subscriptions/listen and every server-to-client notification           ║
// ║   - the io.modelcontextprotocol/tasks extension (needs durable state)      ║
// ║                                                                            ║
// ║  Do not modify unless extending the framework itself.                      ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

// ── Protocol Constants ───────────────────────────────────────────────────────

/// <summary>Reserved _meta keys, extension identifiers, and URI schemes defined by MCP.</summary>
public static class McpKeys
{
    public const string ProtocolVersion = "io.modelcontextprotocol/protocolVersion";
    public const string ClientInfo = "io.modelcontextprotocol/clientInfo";
    public const string ClientCapabilities = "io.modelcontextprotocol/clientCapabilities";
    public const string ServerInfo = "io.modelcontextprotocol/serverInfo";
    public const string LogLevel = "io.modelcontextprotocol/logLevel";
    public const string SubscriptionId = "io.modelcontextprotocol/subscriptionId";

    // Official extensions, advertised through capabilities.extensions.
    public const string ExtensionTasks = "io.modelcontextprotocol/tasks";
    public const string ExtensionUi = "io.modelcontextprotocol/ui";

    /// <summary>MIME type that marks a resource as an MCP Apps view.</summary>
    public const string UiMimeType = "text/html;profile=mcp-app";

    // Agent Skills over MCP.
    public const string SkillScheme = "skill://";
    public const string SkillIndexUri = "skill://index.json";
}

// ── Configuration Types ──────────────────────────────────────────────────────

/// <summary>Server identity reported by server/discover and in each result's _meta.</summary>
public class McpServerInfo
{
    public string Name { get; set; } = "mcp-server";
    public string Version { get; set; } = "1.0.0";
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>Optional icons array, per the icons field in the MCP base spec.</summary>
    public JArray Icons { get; set; }
}

/// <summary>Capabilities advertised by server/discover and initialize.</summary>
public class McpCapabilities
{
    public bool Tools { get; set; } = true;
    public bool Resources { get; set; }
    public bool Prompts { get; set; }
    public bool Completions { get; set; }

    /// <summary>Deprecated in 2026-07-28. Advertised to legacy clients only.</summary>
    public bool Logging { get; set; }

    /// <summary>
    /// Optional extension map keyed by extension identifier, for example
    /// { "io.modelcontextprotocol/ui": { "mimeTypes": [ "text/html;profile=mcp-app" ] } }.
    /// </summary>
    public JObject Extensions { get; set; }
}

/// <summary>Top-level configuration for the MCP handler.</summary>
public class McpServerOptions
{
    public McpServerInfo ServerInfo { get; set; } = new McpServerInfo();

    /// <summary>Preferred revision. Listed first by server/discover.</summary>
    public string ProtocolVersion { get; set; } = "2026-07-28";

    /// <summary>Every revision this server accepts. Must contain ProtocolVersion.</summary>
    public List<string> SupportedProtocolVersions { get; set; } =
        new List<string> { "2026-07-28", "2025-11-25", "2025-06-18" };

    public McpCapabilities Capabilities { get; set; } = new McpCapabilities();

    /// <summary>Natural-language guidance for the model, returned by server/discover.</summary>
    public string Instructions { get; set; }

    // ── Cache hints (2026-07-28 CacheableResult) ──────────────────────────

    /// <summary>Freshness hint in milliseconds for tools/prompts/resources list results.</summary>
    public int ListCacheTtlMs { get; set; } = 300000;

    /// <summary>"public" when list results are identical for every caller, otherwise "private".</summary>
    public string ListCacheScope { get; set; } = "public";

    public int ResourceCacheTtlMs { get; set; } = 60000;

    /// <summary>"private" by default: resource content commonly varies by authenticated user.</summary>
    public string ResourceCacheScope { get; set; } = "private";

    public int DiscoverCacheTtlMs { get; set; } = 3600000;

    /// <summary>
    /// Compare the Mcp-Method and Mcp-Name headers against the request body and reject a
    /// mismatch with HeaderMismatch. Absent headers are ignored, because the Power Platform
    /// gateway makes no guarantee that it forwards them.
    /// </summary>
    public bool ValidateRequestHeaders { get; set; } = true;
}

// ── Transport Headers ────────────────────────────────────────────────────────

/// <summary>
/// The Streamable HTTP headers the framework cares about. Captured in the connector
/// entry point and handed to HandleAsync so the framework never touches ScriptBase.
/// </summary>
public class McpTransportHeaders
{
    public string ProtocolVersion { get; set; }
    public string Method { get; set; }
    public string Name { get; set; }

    public static McpTransportHeaders FromRequest(HttpRequestMessage request)
    {
        var headers = new McpTransportHeaders();
        if (request == null) return headers;

        headers.ProtocolVersion = FirstOrNull(request, "MCP-Protocol-Version");
        headers.Method = FirstOrNull(request, "Mcp-Method");
        headers.Name = FirstOrNull(request, "Mcp-Name");
        return headers;
    }

    private static string FirstOrNull(HttpRequestMessage request, string headerName)
    {
        IEnumerable<string> values;
        if (!request.Headers.TryGetValues(headerName, out values)) return null;
        foreach (var value in values) return value;
        return null;
    }
}

// ── Error Handling ───────────────────────────────────────────────────────────

/// <summary>JSON-RPC 2.0 and MCP error codes.</summary>
public enum McpErrorCode
{
    // JSON-RPC 2.0.
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    // Legacy implementation-defined range (-32000 to -32019). 2026-07-28 forbids
    // allocating new codes here; this one predates the policy.
    RequestTimeout = -32000,

    // Reserved for the MCP specification (-32020 to -32099).
    HeaderMismatch = -32020,
    MissingRequiredClientCapability = -32021,
    UnsupportedProtocolVersion = -32022
}

/// <summary>
/// Throw from tool methods to surface a structured MCP error.
/// Mirrors ModelContextProtocol.McpException from the official SDK.
/// </summary>
public class McpException : Exception
{
    public McpErrorCode Code { get; }
    public McpException(McpErrorCode code, string message) : base(message) => Code = code;
}

// ── Schema Builder (Fluent API) ──────────────────────────────────────────────

/// <summary>
/// Fluent builder for the JSON Schema objects used in tool inputSchema and outputSchema.
///
/// Deliberately narrower than JSON Schema 2020-12 permits. Copilot Studio drops any tool
/// whose schema contains a reference type, and truncates schemas that use multi-type
/// arrays, so this builder never emits $ref, $defs, or "type": [...]. Nested objects are
/// inlined instead. If you hand-write a schema, keep to the same rules — and if you add
/// numeric bounds, note that Copilot Studio expects the draft-04 boolean form of
/// exclusiveMinimum, not the 2020-12 numeric form.
/// </summary>
public class McpSchemaBuilder
{
    private readonly JObject _properties = new JObject();
    private readonly JArray _required = new JArray();

    public McpSchemaBuilder String(string name, string description, bool required = false, string format = null, string[] enumValues = null)
    {
        var prop = new JObject { ["type"] = "string", ["description"] = description };
        if (format != null) prop["format"] = format;
        if (enumValues != null) prop["enum"] = new JArray(enumValues);
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Integer(string name, string description, bool required = false, int? defaultValue = null)
    {
        var prop = new JObject { ["type"] = "integer", ["description"] = description };
        if (defaultValue.HasValue) prop["default"] = defaultValue.Value;
        _properties[name] = prop;
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Number(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "number", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Boolean(string name, string description, bool required = false)
    {
        _properties[name] = new JObject { ["type"] = "boolean", ["description"] = description };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Array(string name, string description, JObject itemSchema, bool required = false)
    {
        _properties[name] = new JObject
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = itemSchema
        };
        if (required) _required.Add(name);
        return this;
    }

    public McpSchemaBuilder Object(string name, string description, Action<McpSchemaBuilder> nestedConfig, bool required = false)
    {
        var nested = new McpSchemaBuilder();
        nestedConfig?.Invoke(nested);
        var obj = nested.Build();
        obj["description"] = description;
        _properties[name] = obj;
        if (required) _required.Add(name);
        return this;
    }

    public JObject Build()
    {
        var schema = new JObject
        {
            ["type"] = "object",
            ["properties"] = _properties
        };

        // Recommended empty-schema form for a tool that takes no parameters.
        if (_properties.Count == 0) schema["additionalProperties"] = false;
        if (_required.Count > 0) schema["required"] = _required;

        return schema;
    }
}

// ── Request Context ──────────────────────────────────────────────────────────

/// <summary>
/// Everything the framework knows about one request. This replaces the connection
/// state that 2026-07-28 removed: era, negotiated version, client identity, and the
/// answers a client attaches when it retries a multi round-trip request.
/// </summary>
public class McpRequestContext
{
    public JToken Id { get; set; }
    public string Method { get; set; }

    /// <summary>True when the caller speaks 2026-07-28 or later.</summary>
    public bool IsModern { get; set; }

    public string ProtocolVersion { get; set; }
    public JObject ClientInfo { get; set; }
    public JObject ClientCapabilities { get; set; }
    public string LogLevel { get; set; }

    /// <summary>Answers supplied on an MRTR retry, keyed by input request name.</summary>
    public JObject InputResponses { get; set; }

    /// <summary>Opaque state handed back with a previous input_required result.</summary>
    public string RequestState { get; set; }

    /// <summary>True when the client declared the given extension identifier.</summary>
    public bool SupportsExtension(string extensionId)
    {
        var extensions = ClientCapabilities?["extensions"] as JObject;
        return extensions != null && extensions[extensionId] != null;
    }
}

// ── Agent Skills ─────────────────────────────────────────────────────────────

/// <summary>One resource file bundled with a skill.</summary>
internal class McpSkillResource
{
    public string Path { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public Func<string> Content { get; set; }
}

/// <summary>Fluent collector for a skill's sibling resource files.</summary>
public class McpSkillResourceBuilder
{
    internal readonly List<McpSkillResource> Resources = new List<McpSkillResource>();

    /// <summary>
    /// Add a file served alongside SKILL.md. Use the relative path the skill body
    /// references, for example "references/policy.md" or "assets/template.md".
    /// </summary>
    public McpSkillResourceBuilder Resource(string path, string description, Func<string> content, string mimeType = "text/markdown")
    {
        Resources.Add(new McpSkillResource
        {
            Path = path,
            Description = description,
            MimeType = mimeType,
            Content = content
        });
        return this;
    }
}

internal class McpSkillDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Instructions { get; set; }
    public string License { get; set; }
    public string Compatibility { get; set; }
    public JObject Metadata { get; set; }
    public List<McpSkillResource> Resources { get; set; } = new List<McpSkillResource>();

    public string SkillUri { get { return McpKeys.SkillScheme + Name + "/SKILL.md"; } }

    public string ResourceUri(string path) { return McpKeys.SkillScheme + Name + "/" + path; }

    /// <summary>Render SKILL.md: YAML frontmatter followed by the markdown body.</summary>
    public string RenderSkillMarkdown()
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        sb.Append("name: ").Append(Name).Append('\n');
        sb.Append("description: ").Append(EscapeYaml(Description)).Append('\n');

        if (!string.IsNullOrWhiteSpace(License))
            sb.Append("license: ").Append(EscapeYaml(License)).Append('\n');

        if (!string.IsNullOrWhiteSpace(Compatibility))
            sb.Append("compatibility: ").Append(EscapeYaml(Compatibility)).Append('\n');

        if (Metadata != null && Metadata.Count > 0)
        {
            sb.Append("metadata:\n");
            foreach (var prop in Metadata.Properties())
                sb.Append("  ").Append(prop.Name).Append(": ").Append(EscapeYaml(prop.Value?.ToString())).Append('\n');
        }

        sb.Append("---\n\n");
        sb.Append(Instructions ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>Quote any scalar that would otherwise break the frontmatter block.</summary>
    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var flattened = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return "\"" + flattened.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}

// ── Internal Tool Registration ───────────────────────────────────────────────

internal class McpToolDefinition
{
    public string Name { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public JObject InputSchema { get; set; }
    public JObject OutputSchema { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
    public Func<JObject, CancellationToken, Task<object>> Handler { get; set; }
}

// ── Internal Resource Registration ───────────────────────────────────────────

internal class McpResourceDefinition
{
    public string Uri { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public JObject Meta { get; set; }
    public Func<CancellationToken, Task<JArray>> Handler { get; set; }
}

internal class McpResourceTemplateDefinition
{
    public string UriTemplate { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string MimeType { get; set; }
    public JObject Annotations { get; set; }
    public Func<string, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── Internal Prompt Registration ─────────────────────────────────────────────

/// <summary>Describes a single prompt argument.</summary>
public class McpPromptArgument
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
}

internal class McpPromptDefinition
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<McpPromptArgument> Arguments { get; set; } = new List<McpPromptArgument>();
    public Func<JObject, CancellationToken, Task<JArray>> Handler { get; set; }
}

// ── McpRequestHandler ────────────────────────────────────────────────────────
//
//    The core bridge class. Stateless, no DI, no ASP.NET Core.
//    Takes a JSON-RPC string in → returns a JSON-RPC string out.
//    This is the class that does not exist in the official SDK today.
//

/// <summary>
/// Stateless MCP request handler that bridges the official SDK's patterns
/// to Power Platform's ScriptBase.ExecuteAsync() model.
/// 
/// Handles all JSON-RPC 2.0 routing, protocol negotiation, tool discovery,
/// parameter binding, and response formatting internally.
/// </summary>
public class McpRequestHandler
{
    private readonly McpServerOptions _options;

    // Dictionaries give O(1) dispatch. The parallel lists preserve registration order,
    // which 2026-07-28 asks for so clients can cache list results reliably.
    private readonly Dictionary<string, McpToolDefinition> _tools;
    private readonly List<McpToolDefinition> _toolOrder;
    private readonly Dictionary<string, McpResourceDefinition> _resources;
    private readonly List<McpResourceDefinition> _resourceOrder;
    private readonly List<McpResourceTemplateDefinition> _resourceTemplates;
    private readonly Dictionary<string, McpPromptDefinition> _prompts;
    private readonly List<McpPromptDefinition> _promptOrder;
    private readonly List<McpSkillDefinition> _skills;

    /// <summary>
    /// Optional logging callback. Wire this up to Application Insights,
    /// Context.Logger, or any other telemetry sink.
    /// </summary>
    public Action<string, object> OnLog { get; set; }

    public McpRequestHandler(McpServerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = new Dictionary<string, McpToolDefinition>(StringComparer.OrdinalIgnoreCase);
        _toolOrder = new List<McpToolDefinition>();
        _resources = new Dictionary<string, McpResourceDefinition>(StringComparer.OrdinalIgnoreCase);
        _resourceOrder = new List<McpResourceDefinition>();
        _resourceTemplates = new List<McpResourceTemplateDefinition>();
        _prompts = new Dictionary<string, McpPromptDefinition>(StringComparer.OrdinalIgnoreCase);
        _promptOrder = new List<McpPromptDefinition>();
        _skills = new List<McpSkillDefinition>();
    }

    // ── Tool Registration ────────────────────────────────────────────────

    /// <summary>
    /// Register a tool using the fluent API.
    /// Define the schema with McpSchemaBuilder, provide a handler, and optionally set annotations.
    /// </summary>
    public McpRequestHandler AddTool(
        string name,
        string description,
        Action<McpSchemaBuilder> schema,
        Func<JObject, CancellationToken, Task<JObject>> handler,
        Action<JObject> annotations = null,
        string title = null,
        Action<McpSchemaBuilder> outputSchemaConfig = null,
        string uiResourceUri = null,
        bool appOnly = false)
    {
        var builder = new McpSchemaBuilder();
        schema?.Invoke(builder);

        JObject annotationsObject = null;
        if (annotations != null)
        {
            annotationsObject = new JObject();
            annotations(annotationsObject);
        }

        JObject outputSchema = null;
        if (outputSchemaConfig != null)
        {
            var outBuilder = new McpSchemaBuilder();
            outputSchemaConfig(outBuilder);
            outputSchema = outBuilder.Build();
        }

        var definition = new McpToolDefinition
        {
            Name = name,
            Title = title,
            Description = description,
            InputSchema = builder.Build(),
            OutputSchema = outputSchema,
            Annotations = annotationsObject,
            Meta = BuildToolUiMeta(uiResourceUri, appOnly),
            Handler = async (args, ct) => await handler(args, ct).ConfigureAwait(false)
        };

        McpToolDefinition previous;
        if (_tools.TryGetValue(name, out previous)) _toolOrder.Remove(previous);

        _tools[name] = definition;
        _toolOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Build the tool _meta that links a tool to its MCP Apps view. appOnly hides the tool
    /// from the model so only the view can call it (for example a card's submit action).
    /// The flat "ui/resourceUri" key is the pre-GA form some hosts still read.
    /// </summary>
    private static JObject BuildToolUiMeta(string uiResourceUri, bool appOnly)
    {
        if (string.IsNullOrWhiteSpace(uiResourceUri)) return null;

        var ui = new JObject { ["resourceUri"] = uiResourceUri };
        if (appOnly) ui["visibility"] = new JArray("app");

        return new JObject { ["ui"] = ui, ["ui/resourceUri"] = uiResourceUri };
    }

    // ── Resource Registration ─────────────────────────────────────────────

    /// <summary>
    /// Register an MCP Apps view. The HTML is served from resources/read with the
    /// text/html;profile=mcp-app MIME type, and uiMeta (csp, permissions, prefersBorder)
    /// is attached under _meta.ui. A view with no csp gets the host's restrictive default:
    /// no network access and no external scripts, which is what these views are written for.
    /// </summary>
    public McpRequestHandler AddUiResource(
        string uri,
        string name,
        string description,
        Func<string> html,
        Action<JObject> uiMeta = null)
    {
        var ui = new JObject();
        uiMeta?.Invoke(ui);

        return AddResource(
            uri, name, description,
            handler: ct => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = McpKeys.UiMimeType,
                    ["text"] = html()
                }
            }),
            mimeType: McpKeys.UiMimeType,
            meta: m => m["ui"] = ui);
    }

    /// <summary>
    /// Register a static resource. The handler returns the resource contents
    /// as a JArray of {uri, text, mimeType} or {uri, blob, mimeType} objects.
    /// </summary>
    public McpRequestHandler AddResource(
        string uri,
        string name,
        string description,
        Func<CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null,
        Action<JObject> meta = null)
    {
        JObject metaObject = null;
        if (meta != null)
        {
            metaObject = new JObject();
            meta(metaObject);
        }

        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        var definition = new McpResourceDefinition
        {
            Uri = uri,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Meta = metaObject,
            Handler = handler
        };

        McpResourceDefinition previous;
        if (_resources.TryGetValue(uri, out previous)) _resourceOrder.Remove(previous);

        _resources[uri] = definition;
        _resourceOrder.Add(definition);

        return this;
    }

    /// <summary>
    /// Register a resource template. The handler receives the resolved URI
    /// and returns the resource contents as a JArray.
    /// </summary>
    public McpRequestHandler AddResourceTemplate(
        string uriTemplate,
        string name,
        string description,
        Func<string, CancellationToken, Task<JArray>> handler,
        string mimeType = "application/json",
        Action<JObject> annotationsConfig = null)
    {
        JObject annotations = null;
        if (annotationsConfig != null)
        {
            annotations = new JObject();
            annotationsConfig(annotations);
        }

        _resourceTemplates.Add(new McpResourceTemplateDefinition
        {
            UriTemplate = uriTemplate,
            Name = name,
            Description = description,
            MimeType = mimeType,
            Annotations = annotations,
            Handler = handler
        });

        return this;
    }

    // ── Prompt Registration ──────────────────────────────────────────────

    /// <summary>
    /// Register a prompt. The handler receives the argument values as a JObject
    /// and returns a JArray of message objects ({role, content: {type, text}}).
    ///
    /// Copilot Studio does not consume MCP prompts. Register them for clients that do.
    /// </summary>
    public McpRequestHandler AddPrompt(
        string name,
        string description,
        List<McpPromptArgument> arguments,
        Func<JObject, CancellationToken, Task<JArray>> handler)
    {
        var definition = new McpPromptDefinition
        {
            Name = name,
            Description = description,
            Arguments = arguments ?? new List<McpPromptArgument>(),
            Handler = handler
        };

        McpPromptDefinition previous;
        if (_prompts.TryGetValue(name, out previous)) _promptOrder.Remove(previous);

        _prompts[name] = definition;
        _promptOrder.Add(definition);

        return this;
    }

    // ── Skill Registration ───────────────────────────────────────────────

    /// <summary>
    /// Publish an Agent Skill over MCP.
    ///
    /// The skill is exposed as ordinary MCP resources under the skill:// scheme, and a
    /// skill://index.json discovery document is generated from every registered skill.
    /// Agent Framework (.NET UseMcpSkills, Python MCPSkillsSource) and Microsoft Foundry
    /// Toolbox read that index and fetch SKILL.md on demand. Only the skill-md
    /// distribution type is produced; archives are not supported.
    /// </summary>
    /// <param name="name">Lowercase letters, digits, and single hyphens. Max 64 characters.</param>
    /// <param name="description">What the skill does and when to use it. Max 1024 characters.</param>
    /// <param name="instructions">The markdown body of SKILL.md. Keep it under 500 lines.</param>
    /// <param name="resources">Sibling files the instructions reference.</param>
    public McpRequestHandler AddSkill(
        string name,
        string description,
        string instructions,
        Action<McpSkillResourceBuilder> resources = null,
        string license = null,
        string compatibility = null,
        JObject metadata = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Skill name is required.");
        if (name.Length > 64)
            throw new ArgumentException($"Skill name '{name}' exceeds 64 characters.");
        if (!Regex.IsMatch(name, "^[a-z0-9]+(-[a-z0-9]+)*$"))
            throw new ArgumentException($"Skill name '{name}' must use lowercase letters, digits, and single hyphens, with no leading or trailing hyphen.");
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException($"Skill '{name}' requires a description.");
        if (description.Length > 1024)
            throw new ArgumentException($"Skill '{name}' description exceeds 1024 characters.");
        if (!string.IsNullOrEmpty(compatibility) && compatibility.Length > 500)
            throw new ArgumentException($"Skill '{name}' compatibility exceeds 500 characters.");

        var resourceBuilder = new McpSkillResourceBuilder();
        resources?.Invoke(resourceBuilder);

        var skill = new McpSkillDefinition
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            License = license,
            Compatibility = compatibility,
            Metadata = metadata,
            Resources = resourceBuilder.Resources
        };

        _skills.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        _skills.Add(skill);

        // The index handler runs at request time, so it always sees every skill
        // registered so far regardless of registration order.
        if (!_resources.ContainsKey(McpKeys.SkillIndexUri))
        {
            AddResource(McpKeys.SkillIndexUri, "Agent Skills index",
                "Discovery document listing the Agent Skills this server publishes.",
                handler: (ct) => Task.FromResult(BuildSkillIndexContents()));
        }

        AddResource(skill.SkillUri, name, description,
            handler: (ct) => Task.FromResult(new JArray
            {
                new JObject
                {
                    ["uri"] = skill.SkillUri,
                    ["mimeType"] = "text/markdown",
                    ["text"] = skill.RenderSkillMarkdown()
                }
            }),
            mimeType: "text/markdown");

        foreach (var resource in skill.Resources)
        {
            var uri = skill.ResourceUri(resource.Path);
            var captured = resource;

            AddResource(uri, resource.Path, resource.Description ?? resource.Path,
                handler: (ct) => Task.FromResult(new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = captured.MimeType,
                        ["text"] = captured.Content != null ? captured.Content() : string.Empty
                    }
                }),
                mimeType: resource.MimeType);
        }

        return this;
    }

    /// <summary>Build the skill://index.json contents from every registered skill.</summary>
    private JArray BuildSkillIndexContents()
    {
        var entries = new JArray();

        foreach (var skill in _skills)
        {
            var entry = new JObject
            {
                ["type"] = "skill-md",
                ["name"] = skill.Name,
                ["description"] = skill.Description,
                ["uri"] = skill.SkillUri
            };

            if (skill.Resources.Count > 0)
            {
                var resourceArray = new JArray();
                foreach (var resource in skill.Resources)
                {
                    resourceArray.Add(new JObject
                    {
                        ["path"] = resource.Path,
                        ["uri"] = skill.ResourceUri(resource.Path),
                        ["description"] = resource.Description,
                        ["mimeType"] = resource.MimeType
                    });
                }
                entry["resources"] = resourceArray;
            }

            entries.Add(entry);
        }

        var index = new JObject { ["skills"] = entries };

        return new JArray
        {
            new JObject
            {
                ["uri"] = McpKeys.SkillIndexUri,
                ["mimeType"] = "application/json",
                ["text"] = index.ToString(Newtonsoft.Json.Formatting.Indented)
            }
        };
    }

    // ── Main Handler ─────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw JSON-RPC 2.0 request string and return a JSON-RPC response string.
    /// This is the single method that bridges the gap.
    /// </summary>
    public Task<string> HandleAsync(string body, CancellationToken cancellationToken)
    {
        return HandleAsync(body, null, cancellationToken);
    }

    /// <summary>
    /// Process a request, using the Streamable HTTP headers to detect the protocol era
    /// and to check Mcp-Method / Mcp-Name against the body.
    /// </summary>
    public async Task<string> HandleAsync(string body, McpTransportHeaders headers, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body))
            return SerializeError(null, McpErrorCode.InvalidRequest, "Empty request body");

        JObject request;
        try
        {
            request = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return SerializeError(null, McpErrorCode.ParseError, "Invalid JSON");
        }

        var ctx = BuildContext(request, headers);

        Log("McpRequestReceived", new
        {
            Method = ctx.Method,
            HasId = ctx.Id != null,
            Era = ctx.IsModern ? "modern" : "legacy",
            ProtocolVersion = ctx.ProtocolVersion
        });

        // 2026-07-28 negotiates per request rather than once per session.
        if (ctx.IsModern && !IsSupportedVersion(ctx.ProtocolVersion))
        {
            Log("McpUnsupportedProtocolVersion", new { Requested = ctx.ProtocolVersion });
            return SerializeError(ctx.Id, McpErrorCode.UnsupportedProtocolVersion,
                "Unsupported protocol version",
                new JObject
                {
                    ["supported"] = new JArray(_options.SupportedProtocolVersions),
                    ["requested"] = ctx.ProtocolVersion
                });
        }

        var headerError = ValidateHeaders(ctx, request, headers);
        if (headerError != null) return headerError;

        try
        {
            switch (ctx.Method)
            {
                // Discovery — servers MUST implement this in 2026-07-28.
                case "server/discover":
                    return HandleDiscover(ctx);

                // The legacy handshake. A client that opens this way is asking for legacy
                // semantics even if it also sent a version marker, so this never 404s.
                case "initialize":
                    return HandleInitialize(ctx, request);

                case "initialized":
                case "notifications/initialized":
                    return SerializeSuccess(ctx, new JObject());

                case "notifications/roots/list_changed":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Removed in 2026-07-28: ping and logging/setLevel are gone, and
                // resource subscriptions moved to the subscriptions/listen stream,
                // which a request/response connector cannot hold open.
                case "ping":
                case "logging/setLevel":
                case "resources/subscribe":
                case "resources/unsubscribe":
                    return ctx.IsModern
                        ? MethodRemoved(ctx, ctx.Method)
                        : SerializeSuccess(ctx, new JObject());

                // Valid in both eras. Copilot Studio expects a JSON-RPC body for every
                // request including notifications, so this always answers.
                case "notifications/cancelled":
                    return SerializeSuccess(ctx, new JObject());

                // Tools
                case "tools/list":
                    return HandleToolsList(ctx);

                case "tools/call":
                    return await HandleToolsCallAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Resources
                case "resources/list":
                    return HandleResourcesList(ctx);

                case "resources/templates/list":
                    return HandleResourceTemplatesList(ctx);

                case "resources/read":
                    return await HandleResourcesReadAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Prompts
                case "prompts/list":
                    return HandlePromptsList(ctx);

                case "prompts/get":
                    return await HandlePromptsGetAsync(ctx, request, cancellationToken).ConfigureAwait(false);

                // Completions
                case "completion/complete":
                    return SerializeSuccess(ctx, new JObject
                    {
                        ["completion"] = new JObject
                        {
                            ["values"] = new JArray(),
                            ["total"] = 0,
                            ["hasMore"] = false
                        }
                    });

                default:
                    Log("McpMethodNotFound", new { Method = ctx.Method });
                    return SerializeError(ctx.Id, McpErrorCode.MethodNotFound, "Method not found", ctx.Method);
            }
        }
        catch (McpException ex)
        {
            Log("McpError", new { Method = ctx.Method, Code = (int)ex.Code, Message = ex.Message });
            return SerializeError(ctx.Id, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            Log("McpError", new { Method = ctx.Method, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    // ── Era Detection ────────────────────────────────────────────────────

    /// <summary>
    /// Decide which protocol era this request belongs to and capture its metadata.
    ///
    /// The spec picks the era from how the client opens the conversation, so an
    /// initialize-style request is always legacy no matter what else it carries — a client
    /// mid-upgrade might send MCP-Protocol-Version while still using the handshake, and
    /// rejecting its initialize would break the connection for no benefit.
    ///
    /// Otherwise a request is modern when it carries a protocol version in _meta or in the
    /// MCP-Protocol-Version header, or when it calls server/discover. Everything else,
    /// Copilot Studio included, is served under legacy semantics.
    /// </summary>
    private McpRequestContext BuildContext(JObject request, McpTransportHeaders headers)
    {
        var paramsObj = request["params"] as JObject;
        var meta = paramsObj?["_meta"] as JObject;
        var method = request.Value<string>("method") ?? string.Empty;

        var metaVersion = meta?[McpKeys.ProtocolVersion]?.ToString();
        var version = !string.IsNullOrWhiteSpace(metaVersion) ? metaVersion : headers?.ProtocolVersion;

        var opensLegacy = method == "initialize"
            || method == "initialized"
            || method == "notifications/initialized";

        var isModern = !opensLegacy
            && (!string.IsNullOrWhiteSpace(version) || method == "server/discover");

        return new McpRequestContext
        {
            Id = request["id"],
            Method = method,
            IsModern = isModern,
            ProtocolVersion = !string.IsNullOrWhiteSpace(version) ? version : _options.ProtocolVersion,
            ClientInfo = meta?[McpKeys.ClientInfo] as JObject,
            ClientCapabilities = meta?[McpKeys.ClientCapabilities] as JObject,
            LogLevel = meta?[McpKeys.LogLevel]?.ToString(),
            InputResponses = paramsObj?["inputResponses"] as JObject,
            RequestState = paramsObj?["requestState"]?.ToString()
        };
    }

    private bool IsSupportedVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return true;

        var supported = _options.SupportedProtocolVersions;
        if (supported == null || supported.Count == 0)
            return string.Equals(version, _options.ProtocolVersion, StringComparison.Ordinal);

        foreach (var candidate in supported)
            if (string.Equals(candidate, version, StringComparison.Ordinal)) return true;

        return false;
    }

    /// <summary>
    /// 2026-07-28 requires Mcp-Method and Mcp-Name on Streamable HTTP so gateways can route
    /// without parsing the body. A header that disagrees with the body is a HeaderMismatch.
    /// An absent header is ignored: the Power Platform gateway makes no guarantee that it
    /// forwards custom headers, and failing closed would break every deployment.
    /// </summary>
    private string ValidateHeaders(McpRequestContext ctx, JObject request, McpTransportHeaders headers)
    {
        if (!ctx.IsModern || !_options.ValidateRequestHeaders || headers == null) return null;

        if (!string.IsNullOrWhiteSpace(headers.Method) &&
            !string.Equals(headers.Method, ctx.Method, StringComparison.Ordinal))
        {
            Log("McpHeaderMismatch", new { Header = headers.Method, Body = ctx.Method });
            return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                "Mcp-Method header does not match the request method",
                new JObject { ["header"] = headers.Method, ["body"] = ctx.Method });
        }

        if (!string.IsNullOrWhiteSpace(headers.Name))
        {
            var bodyName = (request["params"] as JObject)?.Value<string>("name");
            if (!string.IsNullOrWhiteSpace(bodyName) &&
                !string.Equals(headers.Name, bodyName, StringComparison.Ordinal))
            {
                Log("McpHeaderMismatch", new { Header = headers.Name, Body = bodyName });
                return SerializeError(ctx.Id, McpErrorCode.HeaderMismatch,
                    "Mcp-Name header does not match the request target",
                    new JObject { ["header"] = headers.Name, ["body"] = bodyName });
            }
        }

        return null;
    }

    /// <summary>Report a method that 2026-07-28 removed, rather than a bare not-found.</summary>
    private string MethodRemoved(McpRequestContext ctx, string method)
    {
        Log("McpMethodRemoved", new { Method = method, ProtocolVersion = ctx.ProtocolVersion });
        return SerializeError(ctx.Id, McpErrorCode.MethodNotFound,
            $"'{method}' was removed in MCP 2026-07-28", method);
    }

    // ── Protocol Handlers ────────────────────────────────────────────────

    /// <summary>
    /// server/discover — mandatory in 2026-07-28. Reports supported versions,
    /// capabilities, and identity in a single round trip.
    /// </summary>
    private string HandleDiscover(McpRequestContext ctx)
    {
        var result = new JObject
        {
            ["supportedVersions"] = new JArray(_options.SupportedProtocolVersions),
            ["capabilities"] = BuildCapabilities(isModern: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpDiscovered", new
        {
            Server = _options.ServerInfo.Name,
            Versions = _options.SupportedProtocolVersions.Count
        });

        return SerializeSuccess(ctx, result, McpCacheKind.Discover);
    }

    private JObject BuildCapabilities(bool isModern)
    {
        var capabilities = new JObject();

        if (_options.Capabilities.Tools)
            capabilities["tools"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Resources)
        {
            // subscribe is meaningless in the modern era without a subscriptions/listen stream.
            capabilities["resources"] = isModern
                ? new JObject { ["listChanged"] = false }
                : new JObject { ["subscribe"] = false, ["listChanged"] = false };
        }

        if (_options.Capabilities.Prompts)
            capabilities["prompts"] = new JObject { ["listChanged"] = false };

        if (_options.Capabilities.Completions)
            capabilities["completions"] = new JObject();

        // Logging is deprecated in 2026-07-28, so it is offered to legacy clients only.
        if (_options.Capabilities.Logging && !isModern)
            capabilities["logging"] = new JObject();

        if (_options.Capabilities.Extensions != null && _options.Capabilities.Extensions.Count > 0)
            capabilities["extensions"] = _options.Capabilities.Extensions;

        return capabilities;
    }

    private JObject BuildServerInfo(bool includeDetail)
    {
        var serverInfo = new JObject
        {
            ["name"] = _options.ServerInfo.Name,
            ["version"] = _options.ServerInfo.Version
        };

        if (!includeDetail) return serverInfo;

        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Title))
            serverInfo["title"] = _options.ServerInfo.Title;
        if (!string.IsNullOrWhiteSpace(_options.ServerInfo.Description))
            serverInfo["description"] = _options.ServerInfo.Description;
        if (_options.ServerInfo.Icons != null && _options.ServerInfo.Icons.Count > 0)
            serverInfo["icons"] = _options.ServerInfo.Icons;

        return serverInfo;
    }

    /// <summary>The legacy handshake, for clients on 2025-11-25 and earlier.</summary>
    private string HandleInitialize(McpRequestContext ctx, JObject request)
    {
        var clientProtocolVersion = request["params"]?["protocolVersion"]?.ToString();
        var negotiated = !string.IsNullOrWhiteSpace(clientProtocolVersion)
            ? clientProtocolVersion
            : "2025-11-25";

        var result = new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = BuildCapabilities(isModern: false),
            ["serverInfo"] = BuildServerInfo(includeDetail: true)
        };

        if (!string.IsNullOrWhiteSpace(_options.Instructions))
            result["instructions"] = _options.Instructions;

        Log("McpInitialized", new
        {
            Server = _options.ServerInfo.Name,
            Version = _options.ServerInfo.Version,
            ProtocolVersion = negotiated
        });

        return SerializeSuccess(ctx, result);
    }

    private string HandleToolsList(McpRequestContext ctx)
    {
        var toolsArray = new JArray();
        foreach (var tool in _toolOrder)
        {
            var toolObj = new JObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = tool.InputSchema
            };
            if (!string.IsNullOrWhiteSpace(tool.Title))
                toolObj["title"] = tool.Title;
            if (tool.OutputSchema != null)
                toolObj["outputSchema"] = tool.OutputSchema;
            if (tool.Annotations != null && tool.Annotations.Count > 0)
                toolObj["annotations"] = tool.Annotations;
            if (tool.Meta != null && tool.Meta.Count > 0)
                toolObj["_meta"] = tool.Meta;
            toolsArray.Add(toolObj);
        }

        Log("McpToolsListed", new { Count = _toolOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["tools"] = toolsArray }, McpCacheKind.List);
    }

    private string HandleResourcesList(McpRequestContext ctx)
    {
        var resourcesArray = new JArray();
        foreach (var res in _resourceOrder)
        {
            var obj = new JObject
            {
                ["uri"] = res.Uri,
                ["name"] = res.Name
            };
            if (!string.IsNullOrWhiteSpace(res.Description))
                obj["description"] = res.Description;
            if (!string.IsNullOrWhiteSpace(res.MimeType))
                obj["mimeType"] = res.MimeType;
            if (res.Annotations != null && res.Annotations.Count > 0)
                obj["annotations"] = res.Annotations;
            if (res.Meta != null && res.Meta.Count > 0)
                obj["_meta"] = res.Meta;
            resourcesArray.Add(obj);
        }

        Log("McpResourcesListed", new { Count = _resourceOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["resources"] = resourcesArray }, McpCacheKind.List);
    }

    private string HandleResourceTemplatesList(McpRequestContext ctx)
    {
        var templatesArray = new JArray();
        foreach (var tmpl in _resourceTemplates)
        {
            var obj = new JObject
            {
                ["uriTemplate"] = tmpl.UriTemplate,
                ["name"] = tmpl.Name
            };
            if (!string.IsNullOrWhiteSpace(tmpl.Description))
                obj["description"] = tmpl.Description;
            if (!string.IsNullOrWhiteSpace(tmpl.MimeType))
                obj["mimeType"] = tmpl.MimeType;
            if (tmpl.Annotations != null && tmpl.Annotations.Count > 0)
                obj["annotations"] = tmpl.Annotations;
            templatesArray.Add(obj);
        }

        Log("McpResourceTemplatesListed", new { Count = _resourceTemplates.Count });
        return SerializeSuccess(ctx, new JObject { ["resourceTemplates"] = templatesArray }, McpCacheKind.List);
    }

    private async Task<string> HandleResourcesReadAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var uri = paramsObj?.Value<string>("uri");

        if (string.IsNullOrWhiteSpace(uri))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Resource URI is required");

        // 1. Try exact match on registered static resources
        if (_resources.TryGetValue(uri, out var resource))
        {
            Log("McpResourceReadStarted", new { Uri = uri });
            try
            {
                var contents = await resource.Handler(ct).ConfigureAwait(false);

                // The host reads csp and display hints from the content item, not the listing.
                if (resource.Meta != null)
                {
                    foreach (var item in contents.OfType<JObject>())
                        if (item["_meta"] == null) item["_meta"] = resource.Meta.DeepClone();
                }

                Log("McpResourceReadCompleted", new { Uri = uri });
                return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
            }
            catch (Exception ex)
            {
                Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
            }
        }

        // 2. Try matching against registered resource templates
        foreach (var tmpl in _resourceTemplates)
        {
            if (MatchesUriTemplate(tmpl.UriTemplate, uri))
            {
                Log("McpResourceReadStarted", new { Uri = uri, Template = tmpl.UriTemplate });
                try
                {
                    var contents = await tmpl.Handler(uri, ct).ConfigureAwait(false);
                    Log("McpResourceReadCompleted", new { Uri = uri });
                    return SerializeSuccess(ctx, new JObject { ["contents"] = contents }, McpCacheKind.Resource);
                }
                catch (Exception ex)
                {
                    Log("McpResourceReadError", new { Uri = uri, Error = ex.Message });
                    return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
                }
            }
        }

        // 2026-07-28 moved resource-not-found from -32002 to -32602 (Invalid Params).
        return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Resource not found: {uri}");
    }

    /// <summary>
    /// Simple URI template matcher. Checks if a concrete URI matches a template
    /// with {param} placeholders (e.g., "data://records/{id}" matches "data://records/123").
    /// </summary>
    private static bool MatchesUriTemplate(string template, string uri)
    {
        // Split both on '/' and compare segments
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return false;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}")) continue; // wildcard
            if (!string.Equals(seg, uriParts[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>
    /// Extract named parameters from a URI given a template pattern.
    /// E.g., template "data://records/{id}" with uri "data://records/123" returns { "id": "123" }.
    /// </summary>
    public static Dictionary<string, string> ExtractUriParameters(string template, string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var templateParts = template.Split('/');
        var uriParts = uri.Split('/');

        if (templateParts.Length != uriParts.Length) return result;

        for (int i = 0; i < templateParts.Length; i++)
        {
            var seg = templateParts[i];
            if (seg.StartsWith("{") && seg.EndsWith("}"))
            {
                var paramName = seg.Substring(1, seg.Length - 2);
                result[paramName] = uriParts[i];
            }
        }
        return result;
    }

    private string HandlePromptsList(McpRequestContext ctx)
    {
        var promptsArray = new JArray();
        foreach (var prompt in _promptOrder)
        {
            var obj = new JObject
            {
                ["name"] = prompt.Name
            };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                obj["description"] = prompt.Description;

            if (prompt.Arguments.Count > 0)
            {
                var argsArray = new JArray();
                foreach (var arg in prompt.Arguments)
                {
                    var argObj = new JObject { ["name"] = arg.Name };
                    if (!string.IsNullOrWhiteSpace(arg.Description))
                        argObj["description"] = arg.Description;
                    if (arg.Required)
                        argObj["required"] = true;
                    argsArray.Add(argObj);
                }
                obj["arguments"] = argsArray;
            }

            promptsArray.Add(obj);
        }

        Log("McpPromptsListed", new { Count = _promptOrder.Count });
        return SerializeSuccess(ctx, new JObject { ["prompts"] = promptsArray }, McpCacheKind.List);
    }

    private async Task<string> HandlePromptsGetAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var promptName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(promptName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Prompt name is required");

        if (!_prompts.TryGetValue(promptName, out var prompt))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Prompt not found: {promptName}");

        Log("McpPromptGetStarted", new { Prompt = promptName });

        try
        {
            var messages = await prompt.Handler(arguments, ct).ConfigureAwait(false);
            Log("McpPromptGetCompleted", new { Prompt = promptName, MessageCount = messages.Count });

            var result = new JObject { ["messages"] = messages };
            if (!string.IsNullOrWhiteSpace(prompt.Description))
                result["description"] = prompt.Description;

            return SerializeSuccess(ctx, result);
        }
        catch (Exception ex)
        {
            Log("McpPromptGetError", new { Prompt = promptName, Error = ex.Message });
            return SerializeError(ctx.Id, McpErrorCode.InternalError, ex.Message);
        }
    }

    private async Task<string> HandleToolsCallAsync(McpRequestContext ctx, JObject request, CancellationToken ct)
    {
        var paramsObj = request["params"] as JObject;
        var toolName = paramsObj?.Value<string>("name");
        var arguments = paramsObj?["arguments"] as JObject ?? new JObject();

        if (string.IsNullOrWhiteSpace(toolName))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, "Tool name is required");

        if (!_tools.TryGetValue(toolName, out var tool))
            return SerializeError(ctx.Id, McpErrorCode.InvalidParams, $"Unknown tool: {toolName}");

        Log("McpToolCallStarted", new { Tool = toolName, IsRetry = ctx.InputResponses != null });

        try
        {
            var result = await tool.Handler(arguments, ct).ConfigureAwait(false);

            // Multi round-trip request: the tool needs more input before it can finish.
            if (result is JObject marker && marker.Value<bool?>("__mcpInputRequired") == true)
                return SerializeInputRequired(ctx, toolName, marker);

            JObject callResult;

            // Support pre-formatted MCP tool results with rich content types
            // (image, audio, resource, or mixed content arrays).
            // If the handler returns { "content": [ { "type": "..." } ], ... },
            // pass it through directly instead of wrapping in text.
            if (result is JObject jobj && jobj["content"] is JArray contentArray
                && contentArray.Count > 0 && contentArray[0]?["type"] != null)
            {
                callResult = new JObject
                {
                    ["content"] = contentArray,
                    ["isError"] = jobj.Value<bool?>("isError") ?? false
                };

                // structuredContent may be any JSON value as of 2026-07-28.
                if (jobj["structuredContent"] != null)
                    callResult["structuredContent"] = jobj["structuredContent"];
            }
            else
            {
                string text;
                if (result is JObject plainObj)
                    text = plainObj.ToString(Newtonsoft.Json.Formatting.Indented);
                else if (result is string s)
                    text = s;
                else if (result == null)
                    text = "{}";
                else
                    text = JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented);

                callResult = new JObject
                {
                    ["content"] = new JArray { new JObject { ["type"] = "text", ["text"] = text } },
                    ["isError"] = false
                };
            }

            Log("McpToolCallCompleted", new { Tool = toolName, IsError = callResult.Value<bool>("isError") });
            return SerializeSuccess(ctx, callResult);
        }
        catch (ArgumentException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Invalid arguments: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (McpException ex)
        {
            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool error: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
        catch (Exception ex)
        {
            Log("McpToolCallError", new { Tool = toolName, Error = ex.Message });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = $"Tool execution failed: {ex.Message}" }
                },
                ["isError"] = true
            });
        }
    }

    /// <summary>
    /// Emit an input_required result. Legacy clients cannot interpret resultType, so they
    /// receive a tool error naming what the tool still needs rather than a silent failure.
    /// </summary>
    private string SerializeInputRequired(McpRequestContext ctx, string toolName, JObject marker)
    {
        var inputRequests = marker["inputRequests"] as JObject ?? new JObject();

        if (!ctx.IsModern)
        {
            var needed = string.Join(", ", inputRequests.Properties().Select(p => p.Name));
            Log("McpInputRequiredUnsupported", new { Tool = toolName, Needed = needed });

            return SerializeSuccess(ctx, new JObject
            {
                ["content"] = new JArray
                {
                    TextContent($"'{toolName}' needs additional input ({needed}). That requires an MCP client on 2026-07-28 or later; supply the values as tool arguments instead.")
                },
                ["isError"] = true
            });
        }

        var result = new JObject { ["inputRequests"] = inputRequests };

        var requestState = marker.Value<string>("requestState");
        if (!string.IsNullOrWhiteSpace(requestState))
            result["requestState"] = requestState;

        Log("McpInputRequired", new { Tool = toolName, Count = inputRequests.Count });

        return SerializeSuccess(ctx, result, McpCacheKind.None, "input_required");
    }

    // ── Content Helpers ────────────────────────────────────────────────
    //
    //    Use these to build rich tool results with image, audio, or resource
    //    content. Return McpRequestHandler.ToolResult(...) from your handler
    //    to bypass automatic text wrapping.
    //

    /// <summary>Create a text content item.</summary>
    public static JObject TextContent(string text) =>
        new JObject { ["type"] = "text", ["text"] = text };

    /// <summary>Create an image content item (base64-encoded).</summary>
    public static JObject ImageContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "image", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an audio content item (base64-encoded).</summary>
    public static JObject AudioContent(string base64Data, string mimeType) =>
        new JObject { ["type"] = "audio", ["data"] = base64Data, ["mimeType"] = mimeType };

    /// <summary>Create an embedded resource content item.</summary>
    public static JObject ResourceContent(string uri, string text, string mimeType = "text/plain") =>
        new JObject
        {
            ["type"] = "resource",
            ["resource"] = new JObject { ["uri"] = uri, ["text"] = text, ["mimeType"] = mimeType }
        };

    /// <summary>
    /// Create a resource link. Points the client at a resource it can fetch itself,
    /// instead of embedding the bytes in the tool result.
    /// </summary>
    public static JObject ResourceLinkContent(string uri, string name, string description = null, string mimeType = null)
    {
        var link = new JObject { ["type"] = "resource_link", ["uri"] = uri, ["name"] = name };
        if (!string.IsNullOrWhiteSpace(description)) link["description"] = description;
        if (!string.IsNullOrWhiteSpace(mimeType)) link["mimeType"] = mimeType;
        return link;
    }

    /// <summary>
    /// Build a pre-formatted tool result with mixed content types.
    /// Return this from a tool handler to bypass automatic text wrapping.
    /// </summary>
    public static JObject ToolResult(JArray content, JToken structuredContent = null, bool isError = false)
    {
        var result = new JObject { ["content"] = content, ["isError"] = isError };
        if (structuredContent != null) result["structuredContent"] = structuredContent;
        return result;
    }

    // ── Multi Round-Trip Requests ──────────────────────────────────────

    /// <summary>
    /// Ask the client for more input before the tool can finish. Return this from a tool
    /// handler; the client answers by retrying the same tools/call with inputResponses
    /// attached, which arrive on McpRequestContext.InputResponses.
    ///
    /// This is how a stateless server performs elicitation as of 2026-07-28. Because the
    /// server holds no state between the two calls, encode anything you need to resume in
    /// requestState and read it back off the retry.
    /// </summary>
    public static JObject InputRequired(JObject inputRequests, string requestState = null)
    {
        var result = new JObject
        {
            ["__mcpInputRequired"] = true,
            ["inputRequests"] = inputRequests ?? new JObject()
        };
        if (!string.IsNullOrWhiteSpace(requestState)) result["requestState"] = requestState;
        return result;
    }

    /// <summary>Build a single elicitation entry for an InputRequired result.</summary>
    public static JObject ElicitationRequest(string message, Action<McpSchemaBuilder> schemaConfig, string mode = "form")
    {
        var builder = new McpSchemaBuilder();
        schemaConfig?.Invoke(builder);

        return new JObject
        {
            ["method"] = "elicitation/create",
            ["params"] = new JObject
            {
                ["mode"] = mode,
                ["message"] = message,
                ["requestedSchema"] = builder.Build()
            }
        };
    }

    // ── JSON-RPC Serialization ───────────────────────────────────────────

    private string SerializeSuccess(McpRequestContext ctx, JObject result)
    {
        return SerializeSuccess(ctx, result, McpCacheKind.None, "complete");
    }

    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache)
    {
        return SerializeSuccess(ctx, result, cache, "complete");
    }

    /// <summary>
    /// Envelope a successful result. Modern callers additionally get resultType, server
    /// identity in _meta, and cache hints. Legacy callers get exactly the shape they got
    /// before 2026-07-28, which is what keeps Copilot Studio working unchanged.
    /// </summary>
    private string SerializeSuccess(McpRequestContext ctx, JObject result, McpCacheKind cache, string resultType)
    {
        result = result ?? new JObject();

        if (ctx != null && ctx.IsModern)
        {
            result["resultType"] = resultType;

            var meta = result["_meta"] as JObject ?? new JObject();
            meta[McpKeys.ServerInfo] = BuildServerInfo(includeDetail: false);
            result["_meta"] = meta;

            // Only complete results are cacheable; input_required is interim.
            if (resultType == "complete") ApplyCacheHints(result, cache);
        }

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ctx?.Id,
            ["result"] = result
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void ApplyCacheHints(JObject result, McpCacheKind cache)
    {
        switch (cache)
        {
            case McpCacheKind.List:
                result["ttlMs"] = Math.Max(0, _options.ListCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;

            case McpCacheKind.Resource:
                result["ttlMs"] = Math.Max(0, _options.ResourceCacheTtlMs);
                result["cacheScope"] = _options.ResourceCacheScope ?? "private";
                break;

            case McpCacheKind.Discover:
                result["ttlMs"] = Math.Max(0, _options.DiscoverCacheTtlMs);
                result["cacheScope"] = _options.ListCacheScope ?? "public";
                break;
        }
    }

    private string SerializeError(JToken id, McpErrorCode code, string message, JToken data = null)
    {
        var error = new JObject
        {
            ["code"] = (int)code,
            ["message"] = message
        };

        if (data != null && data.Type != JTokenType.Null)
            error["data"] = data;

        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = error
        }.ToString(Newtonsoft.Json.Formatting.None);
    }

    private void Log(string eventName, object data)
    {
        OnLog?.Invoke(eventName, data);
    }
}

/// <summary>Which cache hints a result should carry, per the 2026-07-28 CacheableResult rules.</summary>
public enum McpCacheKind
{
    None,
    List,
    Resource,
    Discover
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 3: GAME VIEWS                                                     ║
// ║                                                                            ║
// ║  The HTML for each ui:// resource. Every game is one self-contained        ║
// ║  document: no CDN, no external fonts, no network calls, so it runs under   ║
// ║  the host's default content security policy.                               ║
// ║                                                                            ║
// ║  The games share the Fluent 2 styles and the host bridge used by the cards ║
// ║  template (BaseCss and Bridge below), so they follow the host's light and  ║
// ║  dark theme. Boards are drawn on a transparent canvas over a themed        ║
// ║  background, or built from elements styled with theme tokens.              ║
// ║                                                                            ║
// ║  Each game offers more than one way to play, because a card in a sandboxed ║
// ║  frame may never receive keyboard focus: arrow keys, swipes, and on-screen ║
// ║  buttons all work. Games pause when the card loses focus or is hidden, and ║
// ║  a finished game reports its score to the agent with ui/update-model-      ║
// ║  context.                                                                  ║
// ║                                                                            ║
// ║  The strings use single quotes only, because they are C# verbatim strings. ║
// ║  Every value from tool data reaches the page through textContent, never    ║
// ║  innerHTML.                                                                ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

internal static class GameViews
{
    private const string Loading =
        "<div id='root'><div class='card' aria-busy='true' aria-label='Loading'>" +
        "<div class='sk' style='width:40%'></div><div class='sk' style='width:85%'></div><div class='sk' style='width:65%'></div></div></div>";

    public static string Snake() { return Page("Snake", GameCss + SnakeCss, Loading, GameJs + SnakeJs); }
    public static string Game2048() { return Page("2048", GameCss + Game2048Css, Loading, GameJs + Game2048Js); }
    public static string Minesweeper() { return Page("Minesweeper", GameCss + W95Css + MinesCss, Loading, GameJs + MinesJs); }
    public static string Tetris() { return Page("Tetris", GameCss + TetrisCss, Loading, GameJs + TetrisJs); }
    public static string CyberTrivia() { return Page("Cyber Trivia", GameCss + TriviaCss, Loading, GameJs + TriviaJs); }
    public static string WordGuess() { return Page("Word Guess", GameCss + WordCss, Loading, GameJs + WordLists + WordJs); }
    public static string Sudoku() { return Page("Sudoku", GameCss + SudokuCss, Loading, GameJs + SudokuJs); }
    public static string Breakout() { return Page("Breakout", GameCss + BreakoutCss, Loading, GameJs + BreakoutJs); }
    public static string Solitaire() { return Page("Solitaire", GameCss + W95Css + SolitaireCss, Loading, GameJs + SolitaireJs); }

    private static string Page(string title, string css, string body, string script)
    {
        return "<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'>" +
            "<meta name='viewport' content='width=device-width, initial-scale=1'>" +
            "<title>" + title + "</title><style>" + BaseCss + css + "</style></head><body>" +
            body + "<script>" + Bridge + script + "</script></body></html>";
    }

    // ── Shared styles ───────────────────────────────────────────────────

    private const string BaseCss = @"
/* Fluent 2 web tokens (light, dark). The host's --color-text-primary, --color-background-primary,
   --color-border-primary and --font-sans win when it provides them, so a card follows its host. */
:root{color-scheme:light dark;
--fg1:var(--color-text-primary,light-dark(#242424,#ffffff));
--fg2:light-dark(#424242,#d6d6d6);
--fg3:light-dark(#616161,#adadad);
--fgd:light-dark(#bdbdbd,#5c5c5c);
--bg1:var(--color-background-primary,light-dark(#ffffff,#292929));
--bg1h:light-dark(#f5f5f5,#3d3d3d);
--bg1p:light-dark(#e0e0e0,#1f1f1f);
--bg3:light-dark(#f5f5f5,#1f1f1f);
--bgs:light-dark(#ebebeb,#333333);
--bg6:light-dark(#e6e6e6,#3d3d3d);
--bgd:light-dark(#f0f0f0,#141414);
--s1:var(--color-border-primary,light-dark(#d1d1d1,#666666));
--s1h:light-dark(#c7c7c7,#757575);
--s2:light-dark(#e0e0e0,#525252);
--sacc:light-dark(#616161,#adadad);
--sd:light-dark(#e0e0e0,#424242);
--brand:light-dark(#0f6cbd,#115ea3);
--brandh:light-dark(#115ea3,#0f6cbd);
--brandp:#0c3b5e;
--brandfg:light-dark(#0f6cbd,#479ef5);
--focus:light-dark(#000000,#ffffff);
--red-fg:light-dark(#b10e1c,#dc626d);--red-bg:light-dark(#fdf3f4,#3f1011);--red-bd:light-dark(#eeacb2,#751d1f);
--grn-fg:light-dark(#0e700e,#54b054);--grn-bg:light-dark(#f1faf1,#052505);--grn-bd:light-dark(#9fd89f,#0e4d0e);
--org-fg:light-dark(#bc4b09,#f98845);--org-bg:light-dark(#fdf6f3,#411200);--org-bd:light-dark(#f4bfab,#7a3405);
--r-m:4px;--r-xl:8px;
--font:var(--font-sans,'Segoe UI','Segoe UI Web (West European)',-apple-system,BlinkMacSystemFont,Roboto,'Helvetica Neue',sans-serif);}
*{box-sizing:border-box}
html,body{margin:0}
body{padding:2px;background:transparent;color:var(--fg1);font-family:var(--font);font-size:14px;line-height:20px;font-weight:400;-webkit-font-smoothing:antialiased}
.card{background:var(--bg1);border:1px solid var(--s2);border-radius:var(--r-xl);padding:16px;max-width:720px}
html[data-mode=fullscreen] body{padding:0}
html[data-mode=fullscreen] .card{max-width:none;min-height:100vh;border:0;border-radius:0;padding:24px}
h1{margin:0;font-size:16px;line-height:22px;font-weight:600}
p{margin:0}
.stack>*+*{margin-top:12px}
.stack>div:empty{display:none}
.cap{font-size:12px;line-height:16px;color:var(--fg3)}
.row{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.row.between{justify-content:space-between}
.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
.btn{display:inline-flex;align-items:center;justify-content:center;gap:6px;min-width:96px;height:32px;padding:0 12px;font:inherit;font-weight:600;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-radius:var(--r-m);cursor:pointer}
.btn:hover:not(:disabled){background:var(--bg1h);border-color:var(--s1h)}
.btn:active:not(:disabled){background:var(--bg1p)}
.btn.primary{color:#ffffff;background:var(--brand);border-color:transparent}
.btn.primary:hover:not(:disabled){background:var(--brandh);border-color:transparent}
.btn.primary:active:not(:disabled){background:var(--brandp)}
.btn:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
.btn.sm{height:24px;min-width:0;padding:0 8px;font-size:12px}
.actions{display:flex;flex-wrap:wrap;gap:8px;margin-top:16px}
input,select,textarea{font:inherit;color:var(--fg1);background:var(--bg1);border:1px solid var(--s1);border-bottom-color:var(--sacc);border-radius:var(--r-m);padding:0 10px;height:32px;width:100%}
textarea{height:auto;min-height:60px;padding:6px 10px;resize:vertical}
input:hover:not(:disabled),select:hover:not(:disabled),textarea:hover:not(:disabled){border-color:var(--s1h);border-bottom-color:var(--sacc)}
input:focus,select:focus,textarea:focus{outline:none;border-bottom-color:var(--brandfg);box-shadow:0 1px 0 0 var(--brandfg)}
input:disabled,select:disabled,textarea:disabled{color:var(--fgd);background:var(--bgd);border-color:var(--sd);cursor:not-allowed}
input::placeholder,textarea::placeholder{color:var(--fg3)}
input[type=checkbox]{width:16px;height:16px;padding:0;accent-color:var(--brand);cursor:pointer}
[aria-invalid=true]{border-color:var(--red-fg)}
.btn:focus-visible,th button:focus-visible,input[type=checkbox]:focus-visible{outline:2px solid var(--focus);outline-offset:1px}
label.lbl{display:block;margin-bottom:4px}
.req{color:var(--red-fg)}
.ic{flex:none;width:16px;height:16px}
.ic .mk{fill:none;stroke:var(--bg1);stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.ic .ln{fill:none;stroke:currentColor;stroke-width:1.5;stroke-linecap:round;stroke-linejoin:round}
.rot{animation:rot 1s linear infinite}
.spin{width:16px;height:16px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;animation:rot .8s linear infinite}
@keyframes rot{to{transform:rotate(360deg)}}
.msg{display:flex;gap:8px;align-items:flex-start;padding:8px 12px;border:1px solid var(--s1);border-radius:var(--r-m);background:var(--bg3)}
.msg .body{flex:1;min-width:0}
.msg .t{font-weight:600}
.msg.error{background:var(--red-bg);border-color:var(--red-bd)}.msg.error>.ic{color:var(--red-fg)}
.msg.success{background:var(--grn-bg);border-color:var(--grn-bd)}.msg.success>.ic{color:var(--grn-fg)}
.msg.warning{background:var(--org-bg);border-color:var(--org-bd)}.msg.warning>.ic{color:var(--org-fg)}
.msg.info>.ic{color:var(--brandfg)}
.badge{display:inline-flex;align-items:center;height:20px;padding:0 8px;border:1px solid transparent;border-radius:999px;font-size:12px;line-height:16px;font-weight:600;text-transform:capitalize}
.sk{height:12px;border-radius:var(--r-m);background:linear-gradient(90deg,var(--bg6) 25%,var(--bg3) 50%,var(--bg6) 75%);background-size:300% 100%;animation:sh 1.5s ease-in-out infinite}
.sk+.sk{margin-top:12px}
@keyframes sh{from{background-position:100% 0}to{background-position:0 0}}
@media (prefers-reduced-motion:reduce){.sk,.rot,.spin{animation-duration:4s}}
";

    // ── Shared host bridge ──────────────────────────────────────────────

    private const string Bridge = @"
var App = (function () {
  var PROTOCOL = '2026-01-26';
  var nextId = 1, pending = {}, handlers = {}, caps = {}, mode = 'inline', modeListeners = [];
  var standalone = window.parent === window;
  var resizeTimer = null, lastHeight = 0;

  function post(message) { window.parent.postMessage(message, '*'); }

  function request(method, params) {
    if (standalone) return Promise.reject(new Error('Not running inside a host'));
    return new Promise(function (resolve, reject) {
      var id = nextId++;
      pending[id] = { resolve: resolve, reject: reject };
      post({ jsonrpc: '2.0', id: id, method: method, params: params || {} });
    });
  }

  function notify(method, params) {
    if (!standalone) post({ jsonrpc: '2.0', method: method, params: params || {} });
  }

  function applyContext(context) {
    if (!context) return;
    var root = document.documentElement;
    if (context.theme) {
      root.setAttribute('data-theme', context.theme);
      root.style.colorScheme = context.theme;
    }
    var vars = context.styles && context.styles.variables;
    if (vars) for (var name in vars) if (vars[name]) root.style.setProperty(name, vars[name]);
    var fonts = context.styles && context.styles.css && context.styles.css.fonts;
    if (fonts) { var style = document.createElement('style'); style.textContent = fonts; document.head.appendChild(style); }
    if (context.displayMode) setMode(context.displayMode);
  }

  function setMode(next) {
    if (next === mode) return;
    mode = next;
    lastHeight = 0;
    document.documentElement.setAttribute('data-mode', mode);
    modeListeners.forEach(function (fn) { fn(mode); });
    resize();
  }

  function requestDisplayMode(next) {
    if (standalone) { setMode(next); return Promise.resolve(mode); }
    return request('ui/request-display-mode', { mode: next }).then(function (result) {
      if (result && result.mode) setMode(result.mode);
      return mode;
    });
  }

  window.addEventListener('message', function (event) {
    if (event.source !== window.parent) return;
    var message = event.data;
    if (!message || message.jsonrpc !== '2.0') return;

    if (message.method) {
      if (message.id !== undefined) {
        if (message.method === 'ping' || message.method === 'ui/resource-teardown') post({ jsonrpc: '2.0', id: message.id, result: {} });
        else post({ jsonrpc: '2.0', id: message.id, error: { code: -32601, message: 'Method not found' } });
      }
      var handler = handlers[message.method];
      if (handler) handler(message.params || {});
      return;
    }

    var waiting = pending[message.id];
    if (!waiting) return;
    delete pending[message.id];
    if (message.error) waiting.reject(new Error(message.error.message || 'Request failed'));
    else waiting.resolve(message.result);
  });

  handlers['ui/notifications/host-context-changed'] = applyContext;

  function measure() {
    var html = document.documentElement, previous = html.style.height;
    html.style.height = 'max-content';
    var height = Math.ceil(html.getBoundingClientRect().height);
    html.style.height = previous;
    return height;
  }

  function resize() {
    if (standalone || mode === 'fullscreen') return;
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(function () {
      // The frame may grow but never shrinks while a game is on screen, so it does not jump between screens.
      var height = Math.max(measure(), lastHeight);
      if (height !== lastHeight) { lastHeight = height; notify('ui/notifications/size-changed', { height: height }); }
    }, 30);
  }

  function connect(name, modes) {
    if (standalone) return Promise.resolve(null);
    return request('ui/initialize', {
      protocolVersion: PROTOCOL,
      appInfo: { name: name, version: '1.0.0' },
      appCapabilities: { availableDisplayModes: modes || ['inline'] }
    }).then(function (result) {
      caps = (result && result.hostCapabilities) || {};
      applyContext(result && result.hostContext);
      notify('ui/notifications/initialized');
      if (window.ResizeObserver) new ResizeObserver(resize).observe(document.body);
      resize();
      return result;
    });
  }

  return {
    isStandalone: standalone,
    connect: connect,
    resize: resize,
    mode: function () { return mode; },
    onMode: function (fn) { modeListeners.push(fn); },
    requestDisplayMode: requestDisplayMode,
    onToolResult: function (fn) { handlers['ui/notifications/tool-result'] = fn; },
    onToolCancelled: function (fn) { handlers['ui/notifications/tool-cancelled'] = fn; },
    canCallTools: function () { return !!caps.serverTools; },
    callTool: function (name, args) { return request('tools/call', { name: name, arguments: args || {} }); },
    sendMessage: function (text) { return request('ui/message', { role: 'user', content: [{ type: 'text', text: text }] }); },
    updateModelContext: function (text, data) { return request('ui/update-model-context', { content: [{ type: 'text', text: text }], structuredContent: data }); },
    openLink: function (url) { return request('ui/open-link', { url: url }); }
  };
})();

function h(tag, attrs) {
  var el = document.createElement(tag);
  if (attrs) for (var key in attrs) {
    var value = attrs[key];
    if (value === null || value === undefined || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'text') el.textContent = value;
    else if (key.indexOf('on') === 0) el.addEventListener(key.slice(2), value);
    else if (key === 'value' || key === 'checked' || key === 'disabled' || key === 'selected') el[key] = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (var i = 2; i < arguments.length; i++) add(el, arguments[i]);
  return el;
}

function add(el, child) {
  if (child === null || child === undefined || child === false) return;
  if (Array.isArray(child)) child.forEach(function (c) { add(el, c); });
  else el.appendChild(typeof child === 'object' ? child : document.createTextNode(String(child)));
}

function clear(el) { while (el.firstChild) el.removeChild(el.firstChild); }

function text(value) { return value === null || value === undefined ? '' : String(value); }

function icon(name) {
  var NS = 'http://www.w3.org/2000/svg';
  var svg = document.createElementNS(NS, 'svg');
  svg.setAttribute('viewBox', '0 0 16 16');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('class', 'ic' + (name === 'running' ? ' rot' : ''));
  function part(tag, attrs) {
    var e = document.createElementNS(NS, tag);
    for (var k in attrs) e.setAttribute(k, attrs[k]);
    svg.appendChild(e);
  }
  var disc = { cx: 8, cy: 8, r: 7, fill: 'currentColor' };
  if (name === 'check') { part('circle', disc); part('path', { d: 'M4.8 8.2l2.1 2.1 4.3-4.4', 'class': 'mk' }); }
  else if (name === 'error') { part('circle', disc); part('path', { d: 'M5.6 5.6l4.8 4.8M10.4 5.6l-4.8 4.8', 'class': 'mk' }); }
  else if (name === 'info') { part('circle', disc); part('path', { d: 'M8 7.3v3.6M8 5v.1', 'class': 'mk' }); }
  else if (name === 'warning') { part('path', { d: 'M8 1.8l6.6 12H1.4z', fill: 'currentColor', stroke: 'currentColor', 'stroke-width': 1.5, 'stroke-linejoin': 'round' }); part('path', { d: 'M8 6.4v3.2M8 11.6v.1', 'class': 'mk' }); }
  else if (name === 'pending') part('circle', { cx: 8, cy: 8, r: 6.25, 'class': 'ln' });
  else if (name === 'running') part('path', { d: 'M8 1.75a6.25 6.25 0 1 1-6.25 6.25', 'class': 'ln' });
  else if (name === 'up') part('path', { d: 'M4 10l4-4 4 4', 'class': 'ln' });
  else if (name === 'down') part('path', { d: 'M4 6l4 4 4-4', 'class': 'ln' });
  else if (name === 'expand') part('path', { d: 'M9.5 2.5h4v4M6.5 13.5h-4v-4M13.5 2.5L9 7M2.5 13.5L7 9', 'class': 'ln' });
  else if (name === 'collapse') part('path', { d: 'M13 3L9.5 6.5M9.5 3.5v3h3M3 13l3.5-3.5M3.5 9.5h3v3', 'class': 'ln' });
  return svg;
}

// Fluent MessageBar. intent is error, success, warning, or info.
function msg(intent, title, body, action) {
  var icons = { error: 'error', success: 'check', warning: 'warning', info: 'info' };
  return h('div', { class: 'msg ' + intent, role: intent === 'error' ? 'alert' : 'status' },
    icon(icons[intent]),
    h('div', { class: 'body' }, title ? h('span', { class: 't', text: title + ' ' }) : null, body ? h('span', { text: body }) : null),
    action || null);
}

// Fluent button loading state: a spinner ahead of the label.
function setBusy(button, busy) {
  var spinner = button.querySelector('.spin');
  if (busy && !spinner) button.insertBefore(h('span', { class: 'spin', 'aria-hidden': 'true' }), button.firstChild);
  if (!busy && spinner) button.removeChild(spinner);
}

function showError(message) {
  var root = document.getElementById('root');
  clear(root);
  root.appendChild(h('div', { class: 'card' }, msg('error', 'Something went wrong.', message)));
  App.resize();
}

function start(name, sample, render, modes) {
  App.onToolResult(function (result) {
    if (result && result.isError) { showError('The card could not be loaded.'); return; }
    var data = result && result.structuredContent;
    if (data) render(data); else showError('The tool returned no data for this card.');
  });
  App.onToolCancelled(function () { showError('The request was cancelled.'); });
  if (App.isStandalone) { render(sample); return; }
  App.connect(name, modes).catch(function () { showError('This card needs a host that supports MCP Apps.'); });
}
";

    // ── Game styles ─────────────────────────────────────────────────────

    private const string GameCss = @"
.stats{display:flex;gap:16px;flex-wrap:wrap}
.stat{display:flex;flex-direction:column;min-width:56px}
.stat strong{font-size:20px;line-height:24px;font-weight:600;font-variant-numeric:tabular-nums}
.boardwrap{display:flex;justify-content:center;touch-action:none;border-radius:var(--r-m)}
.boardwrap:focus{outline:none}
.boardwrap:focus-visible :is(.board,.grid,.mines){outline:2px solid var(--focus);outline-offset:2px}
.board{display:block;height:auto;background:var(--bg3);border:1px solid var(--s1);border-radius:var(--r-m)}
.pad{display:grid;grid-template-columns:repeat(3,52px);gap:8px;justify-content:center}
.pad .btn{min-width:0;height:44px;padding:0;font-size:18px}
.sr{position:absolute;width:1px;height:1px;margin:-1px;overflow:hidden;clip:rect(0 0 0 0);white-space:nowrap}
.stack>div.sr:empty{display:block}
";

    // ── Shared game kit ─────────────────────────────────────────────────
    //
    //    gameShell builds the card around a board: title, stats, board, optional controls,
    //    a status message, and at most two actions (a primary button and Expand). One game
    //    runs per page, so the keyboard, focus, and stop hooks live in a single Active object.

    private const string GameJs = @"
var DIRS = { up: { x: 0, y: -1 }, down: { x: 0, y: 1 }, left: { x: -1, y: 0 }, right: { x: 1, y: 0 } };
var KEYDIR = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', w: 'up', s: 'down', a: 'left', d: 'right' };
var PAD4 = [
  { name: 'up', glyph: '\u25B2', label: 'Up', col: '2', row: 1 },
  { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 2 },
  { name: 'down', glyph: '\u25BC', label: 'Down', col: '2', row: 2 },
  { name: 'right', glyph: '\u25B6', label: 'Right', col: '3', row: 2 }
];
var Active = { key: null, away: null, stop: null, wrap: null };

document.addEventListener('keydown', function (e) {
  if (!Active.key || e.ctrlKey || e.metaKey || e.altKey) return;
  if (Active.key(e.key, e.target === Active.wrap, e.target)) e.preventDefault();
});
document.addEventListener('visibilitychange', function () { if (document.hidden && Active.away) Active.away(); });
window.addEventListener('blur', function () { if (Active.away) Active.away(); });

function rand(n) { return Math.floor(Math.random() * n); }

// Seeded generator, so a puzzle or a deal can be replayed from its number.
function mulberry32(seed) {
  var a = seed >>> 0;
  return function () {
    a = (a + 0x6D2B79F5) >>> 0;
    var t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Fisher-Yates in place. rng defaults to Math.random.
function shuffle(list, rng) {
  var next = rng || Math.random;
  for (var i = list.length - 1; i > 0; i--) {
    var j = Math.floor(next() * (i + 1)), t = list[i];
    list[i] = list[j]; list[j] = t;
  }
  return list;
}

function fmtTime(seconds) {
  var m = Math.floor(seconds / 60), s = seconds % 60;
  return m + ':' + (s < 10 ? '0' : '') + s;
}

// One-second ticker. tick runs each second with the seconds elapsed so far.
function makeTimer(tick) {
  var id = null, seconds = 0;
  return {
    start: function () { if (id) return; id = setInterval(function () { seconds++; tick(seconds); }, 1000); },
    stop: function () { clearInterval(id); id = null; },
    reset: function () { clearInterval(id); id = null; seconds = 0; tick(0); },
    seconds: function () { return seconds; }
  };
}

function clamp(value, min, max, fallback) {
  var n = Number(value);
  if (!isFinite(n)) n = fallback;
  return Math.max(min, Math.min(max, Math.round(n)));
}

function makePad(items, onPress) {
  var pad = h('div', { class: 'pad' });
  items.forEach(function (item) {
    pad.appendChild(h('button', {
      class: 'btn', type: 'button', 'aria-label': item.label, text: item.glyph,
      style: 'grid-column:' + item.col + ';grid-row:' + item.row,
      onclick: function () { onPress(item.name); }
    }));
  });
  return pad;
}

function gameShell(cfg) {
  if (Active.stop) Active.stop();
  Active.key = null; Active.away = null; Active.stop = null;

  var root = document.getElementById('root');
  clear(root);

  var statEls = {};
  var statsRow = h('div', { class: 'stats' });
  cfg.stats.forEach(function (s) {
    var value = h('strong', { text: '0' });
    statEls[s[0]] = value;
    statsRow.appendChild(h('div', { class: 'stat' }, h('span', { class: 'cap', text: s[1] }), value));
    if (cfg.mid && cfg.stats.indexOf(s) === 0) statsRow.appendChild(cfg.mid);
  });

  var wrap = h('div', { class: 'boardwrap', tabindex: '0', role: 'group', 'aria-label': cfg.title + ' board' }, cfg.board);
  var live = h('div', { class: 'sr', 'aria-live': 'polite' });
  var primary = h('button', { class: 'btn primary', type: 'button' });
  var expand = h('button', { class: 'btn', type: 'button' });
  Active.wrap = wrap;

  function paintExpand(mode) {
    var full = mode === 'fullscreen';
    clear(expand);
    expand.appendChild(icon(full ? 'collapse' : 'expand'));
    expand.appendChild(h('span', { text: full ? 'Collapse' : 'Expand' }));
    expand.setAttribute('aria-label', full ? 'Return to the inline view' : 'Open full screen');
  }
  paintExpand(App.mode());
  App.onMode(paintExpand);
  expand.addEventListener('click', function () {
    App.requestDisplayMode(App.mode() === 'fullscreen' ? 'inline' : 'fullscreen').catch(function () { expand.style.display = 'none'; });
  });

  root.appendChild(h('div', { class: 'card stack' + (cfg.cls ? ' ' + cfg.cls : '') },
    h('div', { class: 'row between' }, h('h1', { text: cfg.title }), cfg.screen ? null : statsRow),
    cfg.intro ? h('p', { class: 'intro', text: cfg.intro }) : null,
    cfg.screen ? h('div', { class: 'screen' }, statsRow, wrap) : wrap,
    cfg.controls || null,
    live,
    h('div', { class: 'actions' }, primary, expand)));

  var kit = {
    wrap: wrap,
    stat: function (id, value) { statEls[id].textContent = String(value); },
    label: function (text) { primary.textContent = text; },
    disable: function (off) { primary.disabled = !!off; },
    announce: function (text) { live.textContent = text; },
    focus: function () { try { wrap.focus({ preventScroll: true }); } catch (e) { wrap.focus(); } },
    onPrimary: function (fn) { primary.addEventListener('click', function () { fn(); kit.focus(); }); },
    onKey: function (fn) { Active.key = fn; },
    onAway: function (fn) { Active.away = fn; },
    onStop: function (fn) { Active.stop = fn; },
    onSwipe: function (fn) {
      var sx = 0, sy = 0, live = false;
      wrap.addEventListener('pointerdown', function (e) { sx = e.clientX; sy = e.clientY; live = true; });
      wrap.addEventListener('pointercancel', function () { live = false; });
      wrap.addEventListener('pointerup', function (e) {
        if (!live) return;
        live = false;
        var dx = e.clientX - sx, dy = e.clientY - sy;
        if (Math.max(Math.abs(dx), Math.abs(dy)) < 24) return;
        fn(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
      });
    },
    report: function (message, data) { App.updateModelContext(message, data || {}).catch(function () {}); }
  };

  wrap.addEventListener('pointerdown', function () { kit.focus(); });
  return kit;
}
";

    // ── Snake ───────────────────────────────────────────────────────────

    private const string SnakeCss = @"
:root{--bw:360px}
html[data-mode=fullscreen]{--bw:560px}
html[data-mode=fullscreen] body{padding:24px;background:#2b3034}
html[data-mode=fullscreen] .card.gsnake{max-width:640px;min-height:0;margin:0 auto;border-radius:36px;padding:24px}
.board{width:100%;max-width:var(--bw)}
.gsnake{--lcd:#9fcb12;--ink:#1b3300;position:relative;max-width:420px;margin:0 auto;padding:36px 24px 34px;color:#e4e6e3;background:radial-gradient(120% 60% at 50% 0%,rgba(255,255,255,.16),transparent 60%),linear-gradient(160deg,#6f7a84,#4e565d 55%,#3e454b);border:0;border-radius:48px 48px 76px 76px/44px 44px 96px 96px;box-shadow:inset 0 2px 0 rgba(255,255,255,.3),inset 0 -6px 12px rgba(0,0,0,.35),inset 6px 0 10px rgba(0,0,0,.18),inset -6px 0 10px rgba(0,0,0,.18),0 4px 10px rgba(0,0,0,.4)}
.gsnake::before{content:'';position:absolute;top:14px;left:50%;width:60px;height:7px;margin-left:-30px;border-radius:4px;background:#2a2f34;box-shadow:inset 0 2px 2px rgba(0,0,0,.7),0 1px 0 rgba(255,255,255,.25)}
.gsnake .row.between{gap:0}
.gsnake h1{flex:1 1 100%;margin-bottom:10px;font-size:13px;line-height:18px;font-weight:800;letter-spacing:.4em;text-align:center;text-transform:uppercase;color:#e4e8eb;text-shadow:0 -1px 0 rgba(0,0,0,.5)}
.gsnake .screen{padding:14px 16px;background:linear-gradient(180deg,#3b4147,#252a2f);border-radius:26px 26px 22px 22px;box-shadow:inset 0 2px 5px rgba(0,0,0,.7),0 0 0 2px #5a636b,0 1px 0 2px rgba(255,255,255,.18)}
.gsnake .stats{flex:1 1 100%;flex-wrap:nowrap;justify-content:space-between;gap:12px;padding:10px 12px 6px;background:var(--lcd);border-radius:6px 6px 0 0;box-shadow:inset 0 3px 5px rgba(0,0,0,.4)}
.gsnake .stat{flex-direction:row;align-items:baseline;gap:6px;min-width:0}
.gsnake .stat .cap{font-family:'Courier New',monospace;font-size:11px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;color:var(--ink)}
.gsnake .stat strong{font-family:'Courier New',monospace;font-size:22px;line-height:24px;font-weight:700;letter-spacing:.08em;color:var(--ink)}
.gsnake .screen>.boardwrap{padding:0 12px 12px;background:var(--lcd);border-radius:0 0 6px 6px;box-shadow:inset 0 -3px 5px rgba(0,0,0,.4)}
.gsnake .board{background:var(--lcd);border:2px solid var(--ink);border-radius:0;image-rendering:pixelated}
.gsnake .boardwrap:focus-visible .board{outline-color:var(--ink)}
.gsnake .btn{color:#2b3035;background:linear-gradient(#dde1e4,#a9b0b6);border:1px solid #3f464c;border-radius:20px;box-shadow:0 2px 0 #2c3237,inset 0 1px 0 rgba(255,255,255,.7)}
.gsnake .btn:hover:not(:disabled){background:linear-gradient(#ebeef0,#b9c0c5);border-color:#3f464c}
.gsnake .btn:active:not(:disabled){background:linear-gradient(#a9b0b6,#c4cacf);box-shadow:inset 0 2px 3px rgba(0,0,0,.35);transform:translateY(2px)}
.gsnake .actions{justify-content:center;gap:12px}
.gsnake .btn.primary{min-width:120px}
.gsnake .actions .btn{height:36px}
";

    private const string SnakeJs = @"
function playSnake(data) {
  var N = 20, G = 20;
  var interval = clamp(data.interval, 40, 400, 120);
  var canvas = h('canvas', { class: 'board', width: N * G, height: N * G, role: 'img', 'aria-label': 'Snake board' });
  var ctx = canvas.getContext('2d');
  var kit = gameShell({
    title: 'Snake', board: canvas, cls: 'gsnake', screen: true,
    stats: [['score', 'Score'], ['best', 'Best']],
    controls: makePad(PAD4, steer)
  });
  var INK = '#1b3300';
  var snake, dir, nextDir, food, score = 0, best = 0, state = 'ready', timer = null;

  function digits(n) {
    var s = String(n);
    while (s.length < 4) s = '0' + s;
    return s;
  }

  function place() {
    do { food = { x: rand(N), y: rand(N) }; }
    while (snake.some(function (s) { return s.x === food.x && s.y === food.y; }));
  }

  function reset() {
    snake = [{ x: 10, y: 10 }, { x: 9, y: 10 }, { x: 8, y: 10 }];
    dir = nextDir = DIRS.right;
    score = 0;
    place();
    kit.stat('score', digits(0));
    kit.stat('best', digits(best));
    draw();
  }

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.fillStyle = INK;
    var cx = food.x * G + G / 2, cy = food.y * G + G / 2, r = G / 2 - 2;
    ctx.beginPath();
    ctx.moveTo(cx, cy - r); ctx.lineTo(cx + r, cy); ctx.lineTo(cx, cy + r); ctx.lineTo(cx - r, cy);
    ctx.closePath();
    ctx.fill();
    snake.forEach(function (s) {
      ctx.fillRect(s.x * G + 1, s.y * G + 1, G - 2, G - 2);
    });
    if (state === 'paused' || state === 'over') {
      ctx.fillStyle = 'rgba(159, 203, 18, 0.75)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.fillStyle = INK;
      ctx.strokeStyle = INK;
      ctx.lineWidth = 3;
      ctx.strokeRect(60, 140, canvas.width - 120, 120);
      ctx.font = 'bold 30px ""Courier New"", monospace';
      ctx.textAlign = 'center';
      ctx.fillText(state === 'paused' ? 'PAUSED' : 'GAME OVER', canvas.width / 2, state === 'paused' ? 212 : 188);
      if (state === 'over') {
        ctx.font = 'bold 20px ""Courier New"", monospace';
        ctx.fillText('SCORE ' + digits(score), canvas.width / 2, 232);
      }
    }
  }

  function run() { clearInterval(timer); timer = setInterval(tick, interval); }

  function begin() {
    reset();
    state = 'running';
    kit.label('Pause');
    run();
  }

  function pause() {
    if (state !== 'running') return;
    clearInterval(timer);
    state = 'paused';
    kit.label('Resume');
    draw();
  }

  function resume() {
    state = 'running';
    kit.label('Pause');
    run();
  }

  function act() {
    if (state === 'running') pause();
    else if (state === 'paused') resume();
    else begin();
  }

  function over() {
    clearInterval(timer);
    state = 'over';
    if (score > best) { best = score; kit.stat('best', digits(best)); }
    kit.label('Restart');
    kit.announce('Game over. You scored ' + score + '.');
    kit.report('The user finished a game of Snake with a score of ' + score + '.',     { game: 'snake', score: score });
    draw();
  }

  function tick() {
    dir = nextDir;
    var head = { x: snake[0].x + dir.x, y: snake[0].y + dir.y };
    var eats = head.x === food.x && head.y === food.y;
    var body = eats ? snake : snake.slice(0, -1);
    if (head.x < 0 || head.x >= N || head.y < 0 || head.y >= N ||
        body.some(function (s) { return s.x === head.x && s.y === head.y; })) { over(); return; }
    snake.unshift(head);
    if (eats) { score++; kit.stat('score', digits(score)); place(); } else snake.pop();
    draw();
  }

  function steer(name) {
    var v = DIRS[name];
    if (state === 'ready') {
      begin();
      if (!(v.x === -dir.x && v.y === -dir.y)) dir = nextDir = v;
      return;
    }
    if (state !== 'running') return;
    if (v.x === -dir.x && v.y === -dir.y) return;
    nextDir = v;
  }

  kit.onPrimary(act);
  kit.onSwipe(steer);
  kit.onAway(pause);
  kit.onStop(function () { clearInterval(timer); });
  kit.onKey(function (key, onBoard) {
    var name = KEYDIR[key];
    if (name) { steer(name); return true; }
    if (onBoard && (key === ' ' || key === 'Enter')) { act(); return true; }
    return false;
  });

  kit.label('Start');
  reset();
}

start('Snake', { interval: 120 }, playSnake, ['inline', 'fullscreen']);
";

    // ── 2048 ────────────────────────────────────────────────────────────

    private const string Game2048Css = @"
:root{--bw:310px}
html[data-mode=fullscreen]{--bw:min(600px,calc(100vh - 250px))}
.g2048{position:relative;max-width:400px;margin:8px auto;background:#faf8ee;border:8px solid #1d1d1f;border-radius:30px;box-shadow:0 0 0 2px #5a5a5e,0 4px 12px rgba(0,0,0,.35);color:#776e65;padding:14px 14px 14px}
.g2048::before{content:'';position:absolute;top:-6px;left:50%;width:44px;height:3px;margin-left:-22px;border-radius:2px;background:#4a4a4e}
.g2048>*+*{margin-top:8px}
.g2048 .actions{margin-top:8px}
.g2048 .pad{grid-template-columns:repeat(4,44px);gap:8px}
.g2048 .pad .btn{height:34px;font-size:15px}
html[data-mode=fullscreen] body{padding:24px;background:#2b3034}
html[data-mode=fullscreen] .card.g2048{max-width:760px;min-height:0;margin:0 auto;padding:20px;border:8px solid #1d1d1f;border-radius:30px}
.g2048 .row.between{align-items:stretch;flex-wrap:nowrap;gap:12px}
.g2048 h1{display:flex;align-items:center;justify-content:center;flex:none;min-width:0;padding:8px 12px;font-size:28px;line-height:1;font-weight:800;color:#ffffff;background:#ecc501;border-radius:6px}
.g2048 .stats{flex:1;min-width:0;justify-content:flex-end;gap:8px;flex-wrap:nowrap}
.g2048 .stat{flex:1 1 0;min-width:0;max-width:84px;align-items:center;justify-content:center;padding:6px 6px;background:#bbada0;border-radius:6px}
.g2048 .stat .cap{font-size:12px;font-weight:700;letter-spacing:.04em;text-transform:uppercase;color:#e9e1d7}
.g2048 .stat strong{font-size:22px;color:#ffffff}
.g2048 .intro{margin:0;font-size:15px;font-weight:700;color:#776e65}
.g2048 .btn{color:#ffffff;background:#ec995a;border-color:transparent;border-radius:6px;font-weight:700;letter-spacing:.03em;text-transform:uppercase}
.g2048 .btn:hover:not(:disabled){background:#e88d48;border-color:transparent}
.g2048 .btn:active:not(:disabled){background:#d98040}
.g2048 .boardwrap:focus-visible .grid{outline-color:#ec995a}
.grid{position:relative;container-type:inline-size;display:grid;grid-template-columns:repeat(var(--n),1fr);gap:calc(var(--bw) / 28);width:100%;max-width:var(--bw);padding:calc(var(--bw) / 28);box-sizing:border-box;background:#bbada0;border-radius:6px}
.tile{aspect-ratio:1;display:flex;align-items:center;justify-content:center;border-radius:4px;font-weight:800;font-size:20px;font-size:calc(100cqw / var(--n) / 2.6);background:#ccc0b3;color:transparent}
.tile.d3{font-size:calc(100cqw / var(--n) / 3.2)}
.ovl{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;background:rgba(250,248,238,.78);border-radius:6px}
.ovl p{margin:0;font-size:calc(100cqw / 8);line-height:1.1;font-weight:800;color:#776e65}
.ovl.win{background:rgba(237,194,46,.55)}
.ovl.win p{color:#ffffff}
.tile.d4{font-size:calc(100cqw / var(--n) / 4)}
.tile.d5{font-size:calc(100cqw / var(--n) / 5)}
.t2{background:#efe5da;color:#776e65}
.t4{background:#ece0c9;color:#776e65}
.t8{background:#f1b179;color:#ffffff}
.t16{background:#f59563;color:#ffffff}
.t32{background:#f67c5f;color:#ffffff}
.t64{background:#f65e3b;color:#ffffff}
.t128{background:#edcf72;color:#ffffff}
.t256{background:#edcc61;color:#ffffff}
.t512{background:#edc850;color:#ffffff}
.t1024{background:#edc53f;color:#ffffff}
.t2048{background:#edc22e;color:#ffffff}
.tbig{background:#3c3a32;color:#ffffff}
";

    private const string Game2048Js = @"
function play2048(data) {
  var N = clamp(data.gridSize, 3, 6, 4);
  var board = h('div', { class: 'grid', style: '--n:' + N, role: 'img', 'aria-label': '2048 board' });
  var kit = gameShell({
    title: '2048', board: board, cls: 'g2048',
    intro: 'Join the numbers and get to the 2048 tile!',
    stats: [['score', 'Score'], ['best', 'Best']],
    controls: makePad([
      { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 1 },
      { name: 'up', glyph: '\u25B2', label: 'Up', col: '2', row: 1 },
      { name: 'down', glyph: '\u25BC', label: 'Down', col: '3', row: 1 },
      { name: 'right', glyph: '\u25B6', label: 'Right', col: '4', row: 1 }
    ], move)
  });
  var grid = [], score = 0, best = 0, won = false, over = false;

  function add() {
    var empty = [];
    for (var i = 0; i < grid.length; i++) if (grid[i] === 0) empty.push(i);
    if (empty.length) grid[empty[rand(empty.length)]] = Math.random() < 0.9 ? 2 : 4;
  }

  function reset() {
    grid = [];
    for (var i = 0; i < N * N; i++) grid.push(0);
    score = 0; won = false; over = false;
    add(); add();
    render();
    kit.stat('score', 0);
  }

  function showOverlay(kind) {
    var box = h('div', { class: 'ovl ' + kind }, h('p', { text: kind === 'win' ? 'You win!' : 'Game over!' }));
    var action = h('button', { class: 'btn', type: 'button', text: kind === 'win' ? 'Keep going' : 'Try again' });
    action.addEventListener('click', function () {
      if (kind === 'win') { box.remove(); kit.focus(); } else reset();
    });
    box.appendChild(action);
    board.appendChild(box);
  }

  function render() {
    clear(board);
    var rows = [];
    grid.forEach(function (v, i) {
      var cls = 'tile ' + (v ? 't' + (v > 2048 ? 'big' : v) : 't0') + ' d' + Math.min(5, String(v || 1).length);
      board.appendChild(h('div', { class: cls, text: v || '' }));
      if (i % N === 0) rows.push([]);
      rows[rows.length - 1].push(v || 'empty');
    });
    board.setAttribute('aria-label', '2048 board. ' + rows.map(function (r) { return r.join(', '); }).join('; '));
  }

  function slide(line) {
    var a = line.filter(function (x) { return x !== 0; });
    for (var i = 0; i < a.length - 1; i++) {
      if (a[i] === a[i + 1]) { a[i] *= 2; score += a[i]; a.splice(i + 1, 1); }
    }
    while (a.length < N) a.push(0);
    return a;
  }

  function canMove() {
    for (var i = 0; i < grid.length; i++) {
      if (grid[i] === 0) return true;
      if (i % N < N - 1 && grid[i] === grid[i + 1]) return true;
      if (i + N < grid.length && grid[i] === grid[i + N]) return true;
    }
    return false;
  }

  function move(name) {
    if (over) return;
    var old = grid.slice();
    var vertical = name === 'up' || name === 'down';
    var reverse = name === 'right' || name === 'down';
    for (var k = 0; k < N; k++) {
      var line = [];
      for (var j = 0; j < N; j++) line.push(vertical ? grid[j * N + k] : grid[k * N + j]);
      if (reverse) line.reverse();
      line = slide(line);
      if (reverse) line.reverse();
      for (var m = 0; m < N; m++) {
        if (vertical) grid[m * N + k] = line[m]; else grid[k * N + m] = line[m];
      }
    }
    var moved = grid.some(function (v, i) { return v !== old[i]; });
    if (!moved) return;
    add();
    render();
    kit.stat('score', score);
    if (score > best) { best = score; kit.stat('best', best); }
    if (!won && grid.indexOf(2048) !== -1) {
      won = true;
      showOverlay('win');
      kit.announce('You reached 2048!');
      kit.report('The user reached 2048 with a score of ' + score + '.', { game: '2048', score: score, won: true });
    } else if (!canMove()) {
      over = true;
      showOverlay('over');
      kit.announce('Game over. You scored ' + score + '.');
      kit.report('The user finished a game of 2048 with a score of ' + score + '.', { game: '2048', score: score, won: won });
    }
  }

  kit.label('New game');
  kit.onPrimary(reset);
  kit.onSwipe(move);
  kit.onKey(function (key) {
    var name = KEYDIR[key];
    if (!name) return false;
    move(name);
    return true;
  });
  reset();
}

start('2048', { gridSize: 4 }, play2048, ['inline', 'fullscreen']);
";

    // ── Minesweeper ─────────────────────────────────────────────────────

    // ── Windows 95 chrome ───────────────────────────────────────────────
    //
    //    The raised and sunken bevels, the title bar, and the buttons. A game opts in with
    //    cls: 'gname w95', then adds its own sizing and board styles after this block.

    private const string W95Css = @"
html[data-mode=fullscreen] body{padding:24px;background:#008080}
html[data-mode=fullscreen] .card.w95{max-width:100%;min-height:0;margin:0 auto;padding:3px}
.w95{--raised:inset -1px -1px #0a0a0a,inset 1px 1px #ffffff,inset -2px -2px #808080,inset 2px 2px #dfdfdf;--sunken:inset 1px 1px #0a0a0a,inset -1px -1px #ffffff,inset 2px 2px #808080,inset -2px -2px #dfdfdf;background:#c0c0c0;color:#000000;border:0;border-radius:0;padding:3px;box-shadow:var(--raised)}
.w95 .row.between{flex-wrap:wrap;gap:0}
.w95 h1{display:flex;align-items:center;justify-content:space-between;flex:1 1 100%;min-height:20px;padding:2px 3px 2px 6px;font-size:14px;line-height:16px;font-weight:700;color:#ffffff;background:#000080}
.w95 h1::after{content:'';flex:none;width:50px;height:14px;background:url('data:image/svg+xml,%3Csvg%20xmlns=%22http://www.w3.org/2000/svg%22%20width=%2250%22%20height=%2214%22%20shape-rendering=%22crispEdges%22%3E%3Cg%20id=%22b%22%3E%3Crect%20width=%2216%22%20height=%2214%22%20fill=%22%230a0a0a%22/%3E%3Crect%20width=%2215%22%20height=%2213%22%20fill=%22%23fff%22/%3E%3Crect%20x=%221%22%20y=%221%22%20width=%2214%22%20height=%2212%22%20fill=%22%23808080%22/%3E%3Crect%20x=%221%22%20y=%221%22%20width=%2213%22%20height=%2211%22%20fill=%22%23dfdfdf%22/%3E%3Crect%20x=%222%22%20y=%222%22%20width=%2212%22%20height=%2210%22%20fill=%22%23c0c0c0%22/%3E%3C/g%3E%3Cuse%20href=%22%23b%22%20x=%2216%22/%3E%3Cuse%20href=%22%23b%22%20x=%2234%22/%3E%3Crect%20x=%224%22%20y=%229%22%20width=%226%22%20height=%222%22/%3E%3Crect%20x=%2220%22%20y=%223%22%20width=%229%22%20height=%222%22/%3E%3Crect%20x=%2220%22%20y=%225%22%20width=%221%22%20height=%225%22/%3E%3Crect%20x=%2228%22%20y=%225%22%20width=%221%22%20height=%225%22/%3E%3Crect%20x=%2220%22%20y=%229%22%20width=%229%22%20height=%221%22/%3E%3Cpath%20d=%22M38%204L45%2010M45%204L38%2010%22%20stroke=%22%23000%22%20stroke-width=%221.6%22%20shape-rendering=%22auto%22/%3E%3C/svg%3E') no-repeat}
.w95>:not(:first-child){margin-left:6px;margin-right:6px}
.w95>.boardwrap{margin-top:6px}
.w95 .cap{color:#000000}
.w95 .btn{color:#000000;background:#c0c0c0;border:0;border-radius:0;font-weight:400;box-shadow:var(--raised)}
.w95 .btn:hover:not(:disabled){background:#c0c0c0;border-color:transparent}
.w95 .btn:active:not(:disabled),.w95 .btn[aria-pressed=true]{background:#c0c0c0;box-shadow:var(--sunken)}
.w95 .actions{margin-bottom:6px}
";

    // ── Minesweeper ─────────────────────────────────────────────────────

    private const string MinesCss = @"
.boardwrap{touch-action:pan-x pan-y;overflow-x:auto;justify-content:flex-start}
.boardwrap:focus-visible .mines{outline-offset:-2px}
html[data-mode=fullscreen] .card.gmines{width:max-content}
.gmines{width:fit-content;min-width:300px;max-width:100%}
.gmines .stats{flex:1 1 100%;flex-wrap:nowrap;align-items:center;justify-content:space-between;gap:8px;margin:6px 3px 0;padding:5px 6px;box-shadow:var(--sunken)}
.gmines .stat{position:relative;min-width:0;padding:1px 2px;background:#000000;box-shadow:inset 1px 1px #808080,inset -1px -1px #ffffff}
.gmines .stat .cap{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}
.gmines .stat strong{font-family:'Courier New',monospace;font-size:26px;line-height:30px;font-weight:700;letter-spacing:2px;color:#ff0000}
.gmines .face{width:36px;min-width:0;height:36px;padding:0;font-size:22px;line-height:1;background:#c0c0c0;border:0;border-radius:0;box-shadow:var(--raised);cursor:pointer}
.gmines .face:active{box-shadow:var(--sunken)}
.mines{display:grid;grid-template-columns:repeat(var(--w),var(--cs));gap:0;margin:0 auto;width:max-content;background:#c0c0c0;border:3px solid;border-color:#808080 #ffffff #ffffff #808080}
.cell{width:var(--cs);height:var(--cs);padding:0;border:0;border-radius:0;font:inherit;font-size:16px;font-weight:700;line-height:1;background:#c0c0c0;color:#000000;box-shadow:inset 2px 2px #ffffff,inset -2px -2px #808080;cursor:pointer}
.cell:focus-visible{outline:2px solid var(--focus);outline-offset:-4px}
.cell.open{box-shadow:inset 1px 1px #808080;cursor:default}
.cell.mine{background:#c0c0c0}
.cell.boom{background:#ff0000}
.cell.wrong{text-decoration:line-through;opacity:.7}
.c1{color:#0000ff}
.c2{color:#008000}
.c3{color:#ff0000}
.c4{color:#000080}
.c5{color:#800000}
.c6{color:#008080}
.c7{color:#000000}
.c8{color:#808080}
";

    private const string MinesJs = @"
function playMines(data) {
  var BOMB = '\uD83D\uDCA3', FLAG = '\uD83D\uDEA9';
  var SMILE = '\uD83D\uDE42', DEAD = '\uD83D\uDE35', COOL = '\uD83D\uDE0E', OOH = '\uD83D\uDE2E';
  var W = clamp(data.width, 5, 30, 9), H = clamp(data.height, 5, 30, 9), total = W * H;
  var M = clamp(data.mines, 1, total - 9, 10);
  var difficulty = data.difficulty || 'easy';
  var mine = [], count = [], open = [], flag = [], cells = [];
  var state = 'ready', flagMode = false, hit = -1, flags = 0, opened = 0;

  var board = h('div', { class: 'mines', style: '--w:' + W + ';--cs:' + (W > 20 ? 22 : 28) + 'px' });
  var flagBtn = h('button', { class: 'btn sm', type: 'button', 'aria-pressed': 'false', text: 'Flag mode: off' });
  var face = h('button', { class: 'face', type: 'button', 'aria-label': 'New game', text: SMILE });
  var timer = null, seconds = 0;
  var kit = gameShell({
    title: 'Minesweeper', board: board, cls: 'gmines w95', mid: face,
    stats: [['mines', 'Mines left'], ['time', 'Seconds']],
    controls: h('div', { class: 'row' }, flagBtn)
  });

  function led(n) {
    var s = String(Math.min(999, Math.abs(n)));
    while (s.length < 3) s = '0' + s;
    return n < 0 ? '-' + s.slice(1) : s;
  }

  function stopClock() { clearInterval(timer); timer = null; }

  function startClock() {
    stopClock();
    timer = setInterval(function () { seconds++; kit.stat('time', led(seconds)); }, 1000);
  }

  Active.stop = stopClock;

  function neighbors(i) {
    var r = Math.floor(i / W), c = i % W, out = [];
    for (var dr = -1; dr <= 1; dr++) for (var dc = -1; dc <= 1; dc++) {
      if (!dr && !dc) continue;
      var nr = r + dr, nc = c + dc;
      if (nr >= 0 && nr < H && nc >= 0 && nc < W) out.push(nr * W + nc);
    }
    return out;
  }

  function paint(i) {
    var cell = cells[i], cls = 'cell', label = 'Hidden', face = '';
    if (open[i]) {
      cls += ' open';
      if (mine[i]) { cls += i === hit ? ' mine boom' : ' mine'; face = BOMB; label = 'Mine'; }
      else if (count[i]) { cls += ' c' + count[i]; face = String(count[i]); label = count[i] + ' nearby'; }
      else label = 'Empty';
    } else if (flag[i]) {
      cls += ' flag'; face = FLAG; label = 'Flagged';
      if (state === 'lost' && !mine[i]) cls += ' wrong';
    }
    cell.className = cls;
    cell.textContent = face;
    cell.setAttribute('aria-label', 'Row ' + (Math.floor(i / W) + 1) + ', column ' + (i % W + 1) + ', ' + label);
  }

  function paintAll() { for (var i = 0; i < total; i++) paint(i); }

  function placeMines(safe) {
    var banned = {}, around = neighbors(safe);
    banned[safe] = true;
    if (total - 1 - around.length >= M) around.forEach(function (n) { banned[n] = true; });
    var placed = 0;
    while (placed < M) {
      var i = rand(total);
      if (banned[i] || mine[i]) continue;
      mine[i] = true;
      placed++;
    }
    for (var j = 0; j < total; j++) {
      if (!mine[j]) count[j] = neighbors(j).filter(function (n) { return mine[n]; }).length;
    }
  }

  function toggleFlag(i) {
    if (state === 'won' || state === 'lost' || open[i]) return;
    flag[i] = !flag[i];
    flags += flag[i] ? 1 : -1;
    kit.stat('mines', led(M - flags));
    paint(i);
  }

  function lose(i) {
    state = 'lost';
    stopClock();
    face.textContent = DEAD;
    hit = i;
    for (var j = 0; j < total; j++) if (mine[j]) open[j] = true;
    paintAll();
    kit.announce('You hit a mine. Game over.');
    kit.report('The user lost a game of Minesweeper on ' + difficulty + '.', { game: 'minesweeper', difficulty: difficulty, won: false });
  }

  function win() {
    state = 'won';
    stopClock();
    face.textContent = COOL;
    for (var j = 0; j < total; j++) if (mine[j]) flag[j] = true;
    kit.stat('mines', led(0));
    paintAll();
    kit.announce('You win! You cleared the board.');
    kit.report('The user won a game of Minesweeper on ' + difficulty + '.', { game: 'minesweeper', difficulty: difficulty, won: true });
  }

  function reveal(i) {
    if (state === 'won' || state === 'lost' || open[i] || flag[i]) return;
    if (state === 'ready') { placeMines(i); state = 'playing'; startClock(); }
    if (mine[i]) { lose(i); return; }
    var stack = [i];
    while (stack.length) {
      var n = stack.pop();
      if (open[n] || flag[n]) continue;
      open[n] = true;
      opened++;
      paint(n);
      if (count[n] === 0) neighbors(n).forEach(function (m) { if (!open[m]) stack.push(m); });
    }
    if (opened === total - M) win();
  }

  function press(i) { if (flagMode) toggleFlag(i); else reveal(i); }

  function reset() {
    clear(board);
    cells = []; mine = []; count = []; open = []; flag = [];
    state = 'ready'; hit = -1; flags = 0; opened = 0; seconds = 0;
    stopClock();
    face.textContent = SMILE;
    for (var i = 0; i < total; i++) {
      mine.push(false); count.push(0); open.push(false); flag.push(false);
      var cell = h('button', { class: 'cell', type: 'button' });
      cell.addEventListener('click', (function (idx) { return function () { press(idx); }; })(i));
      cell.addEventListener('contextmenu', (function (idx) { return function (e) { e.preventDefault(); toggleFlag(idx); }; })(i));
      cells.push(cell);
      board.appendChild(cell);
      paint(i);
    }
    kit.stat('mines', led(M));
    kit.stat('time', led(0));
  }

  face.addEventListener('click', function () { reset(); kit.focus(); });
  board.addEventListener('pointerdown', function (e) {
    if (e.button === 0 && !flagMode && (state === 'ready' || state === 'playing')) face.textContent = OOH;
  });
  document.addEventListener('pointerup', function () {
    if (face.textContent === OOH) face.textContent = SMILE;
  });

  flagBtn.addEventListener('click', function () {
    flagMode = !flagMode;
    flagBtn.setAttribute('aria-pressed', String(flagMode));
    flagBtn.textContent = 'Flag mode: ' + (flagMode ? 'on' : 'off');
    flagBtn.className = 'btn sm' + (flagMode ? ' primary' : '');
  });

  kit.label('New game');
  kit.onPrimary(reset);
  reset();
}

start('Minesweeper', { difficulty: 'easy', width: 9, height: 9, mines: 10 }, playMines, ['inline', 'fullscreen']);
";

    // ── Tetris ──────────────────────────────────────────────────────────

    private const string TetrisCss = @"
:root{--bw:200px}
html[data-mode=fullscreen]{--bw:300px}
html[data-mode=fullscreen] body{padding:24px;background:#0d0d2b}
html[data-mode=fullscreen] .card.gtetris{max-width:640px;min-height:0;margin:0 auto;padding:20px;border:6px solid transparent;border-radius:16px}
.gtetris{--gold:linear-gradient(135deg,#fff2b8,#c89a2e 35%,#fff2b8 55%,#8a6410);--silver:linear-gradient(135deg,#f2f2ff,#7b7bbd 50%,#f2f2ff);width:fit-content;max-width:100%;margin:10px auto;padding:16px;font-family:'Trebuchet MS','Segoe UI',sans-serif;font-weight:700;color:#ffffff;background:radial-gradient(1.5px 1.5px at 20% 15%,#fff 50%,transparent) 0 0/90px 70px,radial-gradient(1px 1px at 70% 60%,#cfd2ff 50%,transparent) 0 0/60px 80px,linear-gradient(180deg,#34227f,#14134d 55%,#221a66) padding-box,var(--gold) border-box;border:6px solid transparent;border-radius:16px;box-shadow:0 4px 14px rgba(0,0,0,.5)}
.gtetris .row.between{flex-wrap:wrap;gap:12px;align-items:center}
.gtetris h1{flex:1 1 100%;font-size:26px;line-height:30px;font-weight:800;letter-spacing:.2em;text-align:center;text-transform:uppercase;color:#ffd34d;text-shadow:0 2px 0 #3a1c00,0 0 10px rgba(255,211,77,.45)}
@supports ((-webkit-background-clip:text) or (background-clip:text)){.gtetris h1{color:transparent;text-shadow:none;background:linear-gradient(#ffffff,#ffd34d 55%,#d98c00);-webkit-background-clip:text;background-clip:text;filter:drop-shadow(0 2px 0 #3a1c00)}}
.gtetris .stats{flex:1 1 100%;flex-wrap:nowrap;justify-content:space-between;gap:8px}
.gtetris .stat{flex:1;min-width:0;padding:4px 8px;text-align:center;background:linear-gradient(180deg,#1d1d70,#060624) padding-box,var(--silver) border-box;border:3px solid transparent;border-radius:10px}
.gtetris .cap,.gtetris .stat .cap{font-size:11px;letter-spacing:.12em;text-transform:uppercase;color:#c9c6ff}
.gtetris .stat strong{font-size:20px;line-height:24px;color:#ffffff;text-shadow:0 0 6px rgba(160,160,255,.8)}
.tetris{display:flex;flex-wrap:wrap;gap:20px;align-items:flex-start;justify-content:center}
.tetris .board{width:var(--bw);flex:none;background:linear-gradient(rgba(255,255,255,.07) 1px,transparent 1px) 0 0/100% 5% padding-box,linear-gradient(90deg,rgba(255,255,255,.07) 1px,transparent 1px) 0 0/10% 100% padding-box,linear-gradient(#000,#000) padding-box,var(--silver) border-box;border:4px solid transparent;border-radius:8px}
.side{display:flex;flex-direction:column;gap:8px}
.side .pad{grid-template-columns:repeat(3,40px);gap:6px;margin-top:12px;justify-content:start}
.side .pad .btn{height:40px;font-size:16px;border-radius:50%}
.next{width:80px;height:80px;background:linear-gradient(180deg,#1d1d70,#060624) padding-box,var(--silver) border-box;border:3px solid transparent;border-radius:10px}
.gtetris .btn{color:#2a1b6b;background:linear-gradient(#f6f3ff,#b9b2e6);border:2px solid #5a4fa0;border-radius:999px;font-weight:800;letter-spacing:.03em;box-shadow:0 2px 0 #3a2f7a,inset 0 1px 0 #ffffff}
.gtetris .btn.primary{color:#ffffff;background:linear-gradient(#a98af0,#5a3aa8);border-color:#2f1d73}
.gtetris .btn:hover:not(:disabled){background:linear-gradient(#ffffff,#cbc5f2);border-color:#5a4fa0}
.gtetris .btn.primary:hover:not(:disabled){background:linear-gradient(#b99cf6,#6a48bb);border-color:#2f1d73}
.gtetris .btn:active:not(:disabled){background:linear-gradient(#b9b2e6,#9a92d6);box-shadow:inset 0 2px 3px rgba(0,0,0,.35);transform:translateY(2px)}
.gtetris .btn.primary:active:not(:disabled){background:linear-gradient(#5a3aa8,#7a58c8)}
.gtetris .msg{color:#ffffff;font-weight:400;background:rgba(6,6,36,.8);border:2px solid #7b7bbd;border-radius:10px}
.gtetris .boardwrap:focus-visible .board{outline-color:#ffd34d}
";

    private const string TetrisJs = @"
function playTetris(data) {
  var COLS = 10, ROWS = 20, SZ = 24;
  var startLevel = clamp(data.level, 1, 20, 1);
  var PIECES = [
    [[1, 1, 1, 1]],
    [[1, 1], [1, 1]],
    [[0, 1, 0], [1, 1, 1]],
    [[1, 0, 0], [1, 1, 1]],
    [[0, 0, 1], [1, 1, 1]],
    [[1, 1, 0], [0, 1, 1]],
    [[0, 1, 1], [1, 1, 0]]
  ];
  // Tetris Guideline colors in piece order: I, O, T, J, L, S, Z
  var COLORS = ['#00f0f0', '#f0f000', '#a000f0', '#0000f0', '#f0a000', '#00f000', '#f00000'];

  function shade(hex, mode) {
    var n = parseInt(hex.slice(1), 16), out = '#';
    [n >> 16 & 255, n >> 8 & 255, n & 255].forEach(function (v) {
      var s = mode === 'light' ? Math.min(255, v + 153) : mode === 'dark' ? Math.round(v * 0.5) : Math.round(v * 0.9);
      out += (s < 16 ? '0' : '') + s.toString(16);
    });
    return out;
  }

  function face(g, pts, color) {
    g.fillStyle = color;
    g.beginPath();
    g.moveTo(pts[0][0], pts[0][1]);
    for (var i = 1; i < pts.length; i++) g.lineTo(pts[i][0], pts[i][1]);
    g.closePath();
    g.fill();
  }

  function bevel(g, px, py, size, color) {
    var b = Math.max(2, Math.round(size / 8)), x2 = px + size, y2 = py + size;
    g.fillStyle = color;
    g.fillRect(px, py, size, size);
    face(g, [[px, py], [x2, py], [x2 - b, py + b], [px + b, py + b]], shade(color, 'light'));
    face(g, [[px, y2], [x2, y2], [x2 - b, y2 - b], [px + b, y2 - b]], shade(color, 'dark'));
    face(g, [[px, py], [px + b, py + b], [px + b, y2 - b], [px, y2]], shade(color, 'side'));
    face(g, [[x2, py], [x2, y2], [x2 - b, y2 - b], [x2 - b, py + b]], shade(color, 'side'));
  }

  var canvas = h('canvas', { class: 'board', width: COLS * SZ, height: ROWS * SZ, role: 'img', 'aria-label': 'Tetris board' });
  var nextCanvas = h('canvas', { class: 'next', width: 80, height: 80, role: 'img', 'aria-label': 'Next piece' });
  var ctx = canvas.getContext('2d'), nctx = nextCanvas.getContext('2d');
  var pad = makePad([
    { name: 'rotate', glyph: '\u21BB', label: 'Rotate', col: '2', row: 1 },
    { name: 'left', glyph: '\u25C0', label: 'Left', col: '1', row: 2 },
    { name: 'down', glyph: '\u25BC', label: 'Down', col: '2', row: 2 },
    { name: 'right', glyph: '\u25B6', label: 'Right', col: '3', row: 2 },
    { name: 'drop', glyph: 'Drop', label: 'Hard drop', col: '1 / 4', row: 3 }
  ], control);
  var layout = h('div', { class: 'tetris' }, canvas, h('div', { class: 'side' }, h('span', { class: 'cap', text: 'Next' }), nextCanvas, pad));
  var kit = gameShell({
    title: 'Tetris', board: layout, cls: 'gtetris',
    stats: [['score', 'Score'], ['lines', 'Lines'], ['level', 'Level']]
  });

  var grid = [], cur = null, curX = 0, curY = 0, curColor = '', nextShape = null, nextColor = '';
  var score = 0, lines = 0, level = startLevel, state = 'ready', timer = null;

  function newRow() { var row = []; for (var c = 0; c < COLS; c++) row.push(0); return row; }
  function emptyGrid() { var g = []; for (var r = 0; r < ROWS; r++) g.push(newRow()); return g; }

  function pick() {
    var i = rand(PIECES.length);
    nextShape = PIECES[i];
    nextColor = COLORS[i];
  }

  function collides(shape, px, py) {
    for (var r = 0; r < shape.length; r++) for (var c = 0; c < shape[r].length; c++) {
      if (!shape[r][c]) continue;
      var nr = py + r, nc = px + c;
      if (nc < 0 || nc >= COLS || nr >= ROWS) return true;
      if (nr >= 0 && grid[nr][nc]) return true;
    }
    return false;
  }

  function spawn() {
    cur = nextShape; curColor = nextColor;
    pick();
    curX = Math.floor((COLS - cur[0].length) / 2);
    curY = 0;
    drawNext();
    if (collides(cur, curX, curY)) { over(); return false; }
    return true;
  }

  function block(g, x, y, color, size) {
    bevel(g, x * size, y * size, size, color);
  }

  function draw() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (var r = 0; r < ROWS; r++) for (var c = 0; c < COLS; c++) {
      if (grid[r][c]) block(ctx, c, r, grid[r][c], SZ);
    }
    if (cur && (state === 'running' || state === 'paused')) {
      var gy = curY;
      while (!collides(cur, curX, gy + 1)) gy++;
      ctx.globalAlpha = 0.25;
      for (var r1 = 0; r1 < cur.length; r1++) for (var c1 = 0; c1 < cur[r1].length; c1++) {
        if (cur[r1][c1]) block(ctx, curX + c1, gy + r1, curColor, SZ);
      }
      ctx.globalAlpha = 1;
      for (var r2 = 0; r2 < cur.length; r2++) for (var c2 = 0; c2 < cur[r2].length; c2++) {
        if (cur[r2][c2]) block(ctx, curX + c2, curY + r2, curColor, SZ);
      }
    }
    if (state === 'over' || state === 'paused') {
      ctx.fillStyle = 'rgba(0, 0, 0, 0.55)';
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.fillStyle = '#ffffff';
      ctx.font = 'bold 26px system-ui, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillText(state === 'over' ? 'GAME OVER' : 'PAUSED', canvas.width / 2, canvas.height / 2);
      if (state === 'over') {
        ctx.font = 'bold 16px system-ui, sans-serif';
        ctx.fillText('Score ' + score, canvas.width / 2, canvas.height / 2 + 28);
      }
    }
  }

  function drawNext() {
    nctx.clearRect(0, 0, 80, 80);
    var ox = Math.floor((80 - nextShape[0].length * 18) / 2), oy = Math.floor((80 - nextShape.length * 18) / 2);
    for (var r = 0; r < nextShape.length; r++) for (var c = 0; c < nextShape[r].length; c++) {
      if (nextShape[r][c]) bevel(nctx, ox + c * 18, oy + r * 18, 18, nextColor);
    }
  }

  function stats() {
    kit.stat('score', score);
    kit.stat('lines', lines);
    kit.stat('level', level);
  }

  function run() {
    clearInterval(timer);
    if (state === 'running') timer = setInterval(tick, Math.max(50, 500 - level * 40));
  }

  function sweep() {
    var cleared = 0;
    for (var r = ROWS - 1; r >= 0; r--) {
      if (grid[r].every(function (v) { return v !== 0; })) {
        grid.splice(r, 1);
        grid.unshift(newRow());
        cleared++;
        r++;
      }
    }
    if (!cleared) return;
    lines += cleared;
    score += [0, 100, 300, 500, 800][cleared] * level;
    level = startLevel + Math.floor(lines / 10);
    stats();
    run();
  }

  function lock() {
    for (var r = 0; r < cur.length; r++) for (var c = 0; c < cur[r].length; c++) {
      if (cur[r][c] && curY + r >= 0) grid[curY + r][curX + c] = curColor;
    }
    sweep();
    if (spawn()) draw();
  }

  function rotate() {
    var rotated = [];
    for (var c = 0; c < cur[0].length; c++) {
      var row = [];
      for (var r = cur.length - 1; r >= 0; r--) row.push(cur[r][c]);
      rotated.push(row);
    }
    var kicks = [0, -1, 1, -2, 2];
    for (var i = 0; i < kicks.length; i++) {
      if (!collides(rotated, curX + kicks[i], curY)) { cur = rotated; curX += kicks[i]; return; }
    }
  }

  function tick() {
    if (state !== 'running') return;
    if (!collides(cur, curX, curY + 1)) { curY++; draw(); } else lock();
  }

  function control(name) {
    if (state !== 'running') return;
    if (name === 'left' && !collides(cur, curX - 1, curY)) curX--;
    else if (name === 'right' && !collides(cur, curX + 1, curY)) curX++;
    else if (name === 'down' && !collides(cur, curX, curY + 1)) curY++;
    else if (name === 'rotate') rotate();
    else if (name === 'drop') {
      while (!collides(cur, curX, curY + 1)) curY++;
      lock();
      return;
    }
    draw();
  }

  function begin() {
    grid = emptyGrid();
    score = 0; lines = 0; level = startLevel;
    stats();
    pick();
    state = 'running';
    kit.label('Pause');
    if (spawn()) { draw(); run(); }
  }

  function pause() {
    if (state !== 'running') return;
    clearInterval(timer);
    state = 'paused';
    kit.label('Resume');
    draw();
  }

  function resume() {
    state = 'running';
    kit.label('Pause');
    run();
  }

  function act() {
    if (state === 'running') pause();
    else if (state === 'paused') resume();
    else begin();
  }

  function over() {
    clearInterval(timer);
    state = 'over';
    kit.label('Restart');
    kit.announce('Game over. You scored ' + score + ' and cleared ' + lines + (lines === 1 ? ' line.' : ' lines.'));
    kit.report('The user finished a game of Tetris with a score of ' + score + ', ' + lines + ' lines, level ' + level + '.', { game: 'tetris', score: score, lines: lines, level: level });
    draw();
  }

  kit.onPrimary(act);
  kit.onAway(pause);
  kit.onStop(function () { clearInterval(timer); });
  kit.onKey(function (key, onBoard) {
    var name = KEYDIR[key];
    if (name === 'up') name = 'rotate';
    if (name) { control(name); return state === 'running'; }
    if (key === ' ' && state === 'running') { control('drop'); return true; }
    if ((key === 'p' || key === 'P') && (state === 'running' || state === 'paused')) { act(); return true; }
    if (onBoard && (key === ' ' || key === 'Enter')) { act(); return true; }
    return false;
  });

  grid = emptyGrid();
  pick();
  drawNext();
  draw();
  stats();
  kit.label('Start');
}

start('Tetris', { level: 1 }, playTetris, ['inline', 'fullscreen']);
";

    // ── Cyber Trivia ────────────────────────────────────────────────────

    private const string TriviaCss = @"
html[data-mode=fullscreen] body{padding:24px;background:#12002b}
html[data-mode=fullscreen] .card.gtrivia{max-width:760px;min-height:0;margin:0 auto;padding:24px}
.gtrivia{--ink:#1a0033;--yel:#ffe600;--pink:#ff2d95;--cyan:#00d4ff;--lime:#39ff14;--hd:Impact,'Arial Black','Segoe UI',sans-serif;width:calc(100% - 6px);max-width:560px;margin:8px auto 12px;padding:16px;font-family:'Segoe UI','Trebuchet MS',sans-serif;color:#ffffff;background:radial-gradient(90% 50% at 15% 0%,rgba(255,45,149,.45),transparent 65%),radial-gradient(80% 50% at 100% 100%,rgba(0,212,255,.3),transparent 65%),repeating-linear-gradient(135deg,#1c0045 0 14px,#16003a 14px 28px);border:4px solid var(--yel);border-radius:6px;box-shadow:3px 3px 0 var(--pink)}
.gtrivia .row.between{flex-wrap:wrap;gap:8px;align-items:center}
.gtrivia h1{flex:1 1 100%;font-family:var(--hd);font-size:32px;line-height:36px;font-weight:400;letter-spacing:.04em;text-align:center;text-transform:uppercase;color:var(--yel);text-shadow:3px 3px 0 var(--pink);transform:rotate(-1.5deg)}
.gtrivia .stats{flex:1 1 100%;flex-wrap:nowrap;justify-content:center;gap:10px}
.gtrivia .stat{flex:0 1 150px;min-width:0;align-items:center;padding:3px 10px;background:#000000;border:2px solid var(--yel);border-radius:4px}
.gtrivia .stat .cap{font-size:11px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--cyan)}
.gtrivia .stat strong{font-family:'Courier New',monospace;font-size:22px;color:var(--lime)}
.gtrivia .boardwrap{display:block;touch-action:auto}
.gtrivia .boardwrap:focus-visible{outline:3px solid var(--yel);outline-offset:3px}
.tq{overflow-y:auto;overflow-x:hidden;scrollbar-gutter:stable;padding:0 6px 6px 0}
.tq>*+*{margin-top:10px}
.host{position:relative;margin-top:16px;padding:10px 12px;color:var(--ink);background:#ffffff;border:3px solid #000000;border-radius:12px;box-shadow:3px 3px 0 #000000}
.host .who{display:inline-block;margin:-22px 0 4px;padding:1px 8px;font-family:var(--hd);font-size:13px;letter-spacing:.08em;text-transform:uppercase;color:#ffffff;background:var(--pink);border:2px solid #000000;transform:rotate(-2deg)}
.host p{font-size:15px;line-height:21px;font-weight:600}
.rules{margin:0;padding:0 0 0 20px;display:grid;gap:4px;font-weight:600}
.qhead{display:flex;flex-wrap:wrap;align-items:center;justify-content:space-between;gap:6px}
.qcat,.dbl{padding:2px 10px;font-family:var(--hd);font-size:14px;letter-spacing:.06em;text-transform:uppercase;color:var(--ink);background:var(--cyan);border:2px solid #000000;transform:rotate(-1deg)}
.dbl{color:#ffffff;background:var(--pink);transform:rotate(1.5deg)}
.clock{display:flex;align-items:center;gap:10px}
.val{flex:none;min-width:116px;padding:2px 10px;text-align:center;font-family:var(--hd);font-size:34px;line-height:40px;letter-spacing:.03em;font-variant-numeric:tabular-nums;color:var(--lime);background:#000000;border:3px solid var(--yel);border-radius:4px}
.bar{flex:1;height:18px;overflow:hidden;background:#000000;border:2px solid var(--yel);border-radius:2px}
.barfill{height:100%;width:100%;background:var(--lime)}
.barfill.low{background:#ff3b30}
.tq .stem{font-size:18px;line-height:25px;font-weight:700}
.plates{display:grid;gap:8px}
.plate{display:flex;align-items:center;gap:10px;width:100%;min-height:46px;padding:8px 12px 8px 8px;text-align:left;font:inherit;font-weight:700;color:var(--ink);background:var(--yel);border:3px solid #000000;border-radius:4px;box-shadow:3px 3px 0 #000000;cursor:pointer}
.plate:hover:not([aria-disabled=true]){background:#fff59a;transform:translate(-1px,-1px) rotate(-.4deg)}
.plate:focus-visible{outline:3px solid #ffffff;outline-offset:2px}
.plate[aria-disabled=true]{cursor:default}
.plate .ltr{flex:none;display:grid;place-items:center;width:28px;height:28px;font-family:var(--hd);font-size:16px;color:#ffffff;background:var(--pink);border:2px solid #000000;border-radius:50%}
.plate .txt{flex:1;min-width:0}
.plate .mark{flex:none;font-size:20px;font-weight:900}
.plate.ok{background:var(--lime)}
.plate.bad{background:#ff5c8a}
.plate.dim{opacity:.5}
.verdict{display:flex;align-items:baseline;justify-content:space-between;gap:10px;padding:6px 12px;color:var(--ink);border:3px solid #000000;border-radius:4px;box-shadow:3px 3px 0 #000000;transform:rotate(-1deg);animation:pop .35s}
.verdict strong{font-family:var(--hd);font-size:28px;font-weight:400;letter-spacing:.04em}
.verdict .amt{font-family:var(--hd);font-size:24px}
.verdict.win{background:var(--lime)}
.verdict.lose{background:#ff5c8a}
.verdict.late{background:var(--cyan)}
@keyframes pop{0%{transform:scale(.6) rotate(-6deg)}70%{transform:scale(1.06) rotate(-1deg)}100%{transform:scale(1) rotate(-1deg)}}
@media (prefers-reduced-motion:reduce){.verdict{animation:none}.plate:hover:not([aria-disabled=true]){transform:none}}
.fb{padding:10px 12px;background:rgba(0,0,0,.55);border:2px solid rgba(255,255,255,.5);border-radius:4px}
.fb p+p{margin-top:6px}
.fb .act{color:var(--yel);font-weight:600}
.tq .big{font-family:var(--hd);font-size:56px;line-height:60px;text-align:center;color:var(--lime);text-shadow:3px 3px 0 #000000}
.tq .big.neg{color:#ff5c8a}
.tq .rate{text-align:center;font-family:var(--hd);font-size:22px;letter-spacing:.04em;text-transform:uppercase;color:var(--yel)}
.tq .weak{text-align:center;font-weight:600}
.tq .none{text-align:center;color:var(--cyan);font-size:12px}
.miss{margin:0;padding:0;list-style:none;display:grid;gap:8px}
.miss li{padding:8px 10px;background:rgba(0,0,0,.55);border-left:5px solid var(--pink)}
.miss .q{font-weight:700}
.miss .a{color:var(--lime);font-weight:600}
.gtrivia .btn{font-family:var(--hd);font-size:16px;font-weight:400;letter-spacing:.05em;text-transform:uppercase;color:var(--ink);background:var(--yel);border:3px solid #000000;border-radius:4px;box-shadow:3px 3px 0 var(--pink)}
.gtrivia .btn:hover:not(:disabled){background:#fff59a;border-color:#000000}
.gtrivia .btn:active:not(:disabled){transform:translate(2px,2px);box-shadow:1px 1px 0 var(--pink)}
.gtrivia .btn:disabled{color:#9d8fb5;background:#4b3a6b;border-color:#2a1a45;box-shadow:none}
.gtrivia .btn:focus-visible{outline:3px solid #ffffff}
.gtrivia .actions{justify-content:center}
";

    private const string TriviaJs = @"
function playTrivia(data) {
  var qs = Array.isArray(data.questions) ? data.questions.filter(function (q) { return q && q.stem && Array.isArray(q.options) && q.options.length > 1; }) : [];
  if (!qs.length) { showError('No questions came with this quiz.'); return; }
  var LETTERS = ['A', 'B', 'C', 'D'];
  var SECS = 20, timed = data.timed !== false, plain = data.tone === 'plain';
  var QUIPS = {
    hello: [
      'Welcome to Cyber Trivia, the quiz show where your inbox is the real contestant. I am Pat Ching, your host and your mild disappointment.',
      'Hello, contestant! I am Pat Ching. Let\u2019s find out how much you really know about not getting hacked.',
      'Pat Ching here. Today we find out whether you are a security genius or just good at ignoring update reminders.'
    ],
    right: ['Correct! Somebody has been reading the security memos.', 'Yes! Your firewall is proud of you.', 'Right! Attackers hate it when people pay attention.', 'Ding ding ding! That is money in the bank.', 'Nailed it. Your future self and your security team thank you.'],
    wrong: ['Ouch. In real life that one costs a lot more than money.', 'Nope! Good thing this is only a game.', 'Wrong! The attacker sends their regards.', 'Yikes. Do that for real and we are both filling out an incident report.', 'Not quite. Think of it as an expensive lesson.'],
    late: ['Time is up! Hesitation is fine in real life, as long as you do not click while you think.', 'Out of time! The clock waits for no one, and neither does a phishing email.', 'Too slow! The good news is that nobody lost any money. The bad news is that nobody won any.'],
    final: ['Final question! The money is doubled and so is the pressure.', 'Last one, and it is worth double. No pressure. Okay, a little pressure.'],
    close: {
      top: ['You are a security champion. I would say you could run the place, but then you would make me attend the training.', 'Wow. The attackers will be going after somebody else.'],
      good: ['Well prepared! A few rough spots, but nothing a refresher cannot fix.', 'Solid work. You would make a hacker work for it.'],
      mid: ['Getting there! Some of those were close calls, and close calls are how incidents start.', 'Not bad, not great. Review the misses below and come back stronger.'],
      low: ['Rough round, but this is the safe place to learn it. Review the misses below and try again.', 'Well, that is what practice is for. Take a look at what you missed.'],
      broke: ['You finished in the red, but this is the cheapest place to make mistakes. Read the misses below.']
    }
  };
  var board = h('div', { class: 'tq' });
  var kit = gameShell({
    title: 'Cyber Trivia', board: board, cls: 'gtrivia',
    stats: [['cash', 'Cash'], ['q', 'Question']]
  });
  var state = 'intro', idx = 0, cash = 0, right = 0, answered = false, plates = [], missed = [], cats = {};
  var clock = null, left = SECS * 1000, tickAt = 0, running = false, paused = false, curBase = 0;
  var valueEl = null, barEl = null;

  function pick(list) { return list[rand(list.length)]; }

  function money(n) {
    var s = String(Math.abs(Math.round(n))).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    return (n < 0 ? '-$' : '$') + s;
  }

  function baseFor(i) {
    var v = Math.min(2000, 1000 + 100 * i);
    return i === qs.length - 1 && qs.length > 1 ? v * 2 : v;
  }

  function current() { return timed ? Math.max(0, Math.round(curBase * left / (SECS * 1000) / 10) * 10) : curBase; }

  function paintClock() {
    if (!valueEl) return;
    valueEl.textContent = paused ? 'PAUSED' : money(current());
    if (barEl) {
      barEl.style.width = (left / (SECS * 1000) * 100) + '%';
      barEl.className = 'barfill' + (left < 5000 ? ' low' : '');
    }
  }

  function stopClock() { clearInterval(clock); clock = null; running = false; }

  function tick() {
    var now = Date.now();
    left -= now - tickAt;
    tickAt = now;
    if (left <= 0) { left = 0; paintClock(); settle(-1); return; }
    paintClock();
  }

  function resume() {
    if (!timed || state !== 'q' || answered || running) return;
    tickAt = Date.now();
    running = true;
    clearInterval(clock);
    clock = setInterval(tick, 100);
  }

  function pauseClock() {
    if (!running) return;
    left -= Date.now() - tickAt;
    stopClock();
    paused = true;
    paintClock();
  }

  function wake() {
    if (!paused) return;
    paused = false;
    paintClock();
    resume();
  }

  function host(line) {
    if (plain || !line) return null;
    return h('div', { class: 'host' }, h('span', { class: 'who', text: 'Pat Ching' }), h('p', { text: line }));
  }

  function buildIntro(target) {
    var rules = timed
      ? ['Beat the clock. A right answer wins whatever is on the clock.', 'A wrong answer costs the full starting amount.']
      : ['A right answer wins the money. A wrong answer loses it.'];
    rules.push('The final question is worth double.');
    rules.push('Press A to D, or 1 to 4, to buzz in.');
    add(target, host(pick(QUIPS.hello)));
    target.appendChild(h('ul', { class: 'rules' }, rules.map(function (r) { return h('li', { text: r }); })));
  }

  function intro() {
    state = 'intro';
    stopClock();
    clear(board);
    kit.stat('q', '0/' + qs.length);
    kit.stat('cash', money(0));
    buildIntro(board);
    kit.label('Let\u2019s play');
    kit.disable(false);
    App.resize();
  }

  // Builds one question into target. The same builder fills a hidden copy that is used to measure the tallest layout.
  function buildQuestion(i, target, finalLine) {
    var q = qs[i], last = i === qs.length - 1 && qs.length > 1, base = baseFor(i), pl = [];
    var val = h('div', { class: 'val', 'aria-hidden': 'true', text: money(base) });
    var bar = timed ? h('div', { class: 'barfill' }) : null;
    var list = h('div', { class: 'plates', role: 'group', 'aria-label': 'Answers' });
    q.options.slice(0, 4).forEach(function (opt, n) {
      var plate = h('button', { class: 'plate', type: 'button', 'aria-label': LETTERS[n] + ': ' + text(opt), onclick: function () { settle(n); } },
        h('span', { class: 'ltr', 'aria-hidden': 'true', text: LETTERS[n] }),
        h('span', { class: 'txt', text: text(opt) }),
        h('span', { class: 'mark', 'aria-hidden': 'true' }));
      pl.push(plate);
      list.appendChild(plate);
    });
    target.appendChild(h('div', { class: 'qhead' },
      h('span', { class: 'qcat', text: text(q.categoryName || q.category) }),
      last ? h('span', { class: 'dbl', text: 'Final question: double' }) : null));
    if (last) add(target, host(finalLine));
    target.appendChild(h('div', { class: 'clock' }, val, bar ? h('div', { class: 'bar', 'aria-hidden': 'true' }, bar) : null, h('span', { class: 'sr', text: 'Worth ' + money(base) })));
    target.appendChild(h('p', { class: 'stem', text: text(q.stem) }));
    target.appendChild(list);
    target.appendChild(h('div', { class: 'fbwrap' }));
    return { plates: pl, val: val, bar: bar };
  }

  function buildFeedback(wrap, q, late, win, shout, amount, line) {
    wrap.appendChild(h('div', { class: 'verdict ' + (late ? 'late' : win ? 'win' : 'lose'), role: 'status' },
      h('strong', { text: shout }), h('span', { class: 'amt', text: amount })));
    add(wrap, host(line));
    wrap.appendChild(h('div', { class: 'fb' },
      q.explain ? h('p', { text: text(q.explain) }) : null,
      q.action ? h('p', { class: 'act', text: 'What to do: ' + text(q.action) }) : null));
  }

  // Locks the board to the height of the tallest question with its feedback showing, so the card never resizes during play.
  function reserve() {
    if (App.mode() === 'fullscreen') { board.style.height = ''; return; }
    var ghost = h('div', { class: 'tq', 'aria-hidden': 'true', style: 'position:absolute;left:0;right:0;top:0;visibility:hidden;pointer-events:none' });
    var longest = function (list) { return list.reduce(function (a, b) { return b.length > a.length ? b : a; }); };
    var line = longest(QUIPS.right.concat(QUIPS.wrong, QUIPS.late)), best = 0;
    kit.wrap.style.position = 'relative';
    kit.wrap.appendChild(ghost);
    qs.forEach(function (q, i) {
      clear(ghost);
      buildQuestion(i, ghost, longest(QUIPS.final));
      buildFeedback(ghost.querySelector('.fbwrap'), q, false, false, 'Wrong!', '-' + money(baseFor(i)), line);
      best = Math.max(best, ghost.offsetHeight);
    });
    clear(ghost);
    buildIntro(ghost);
    best = Math.max(best, ghost.offsetHeight);
    kit.wrap.removeChild(ghost);
    board.style.height = best + 'px';
    App.resize();
  }

  function show() {
    stopClock();
    state = 'q';
    paused = false;
    clear(board);
    answered = false;
    left = SECS * 1000;
    curBase = baseFor(idx);
    kit.stat('q', (idx + 1) + '/' + qs.length);
    kit.stat('cash', money(cash));
    var built = buildQuestion(idx, board, pick(QUIPS.final));
    plates = built.plates; valueEl = built.val; barEl = built.bar;
    kit.label(idx === qs.length - 1 ? 'See results' : 'Next');
    kit.disable(true);
    paintClock();
    App.resize();
    resume();
  }

  function settle(i) {
    if (state !== 'q' || answered || i >= plates.length) return;
    answered = true;
    stopClock();
    paused = false;
    var q = qs[idx], late = i < 0, win = i === q.correct, name = text(q.categoryName || q.category);
    var worth = current();
    var delta = late ? 0 : win ? worth : -curBase;
    var c = cats[name] || (cats[name] = { n: 0, right: 0 });
    c.n++;
    plates.forEach(function (plate, n) {
      plate.setAttribute('aria-disabled', 'true');
      var mark = plate.querySelector('.mark');
      if (n === q.correct) { plate.className = 'plate ok'; mark.textContent = '\u2713'; }
      else if (n === i) { plate.className = 'plate bad'; mark.textContent = '\u2717'; }
      else plate.className = 'plate dim';
    });
    if (win) { right++; c.right++; } else missed.push(q);
    cash += delta;
    kit.stat('cash', money(cash));
    var shout = late ? 'Time\u2019s up!' : win ? 'Right!' : 'Wrong!';
    var amount = late ? 'No money moves' : (delta > 0 ? '+' : '') + money(delta);
    var wrap = board.querySelector('.fbwrap');
    buildFeedback(wrap, q, late, win, shout, amount, pick(late ? QUIPS.late : win ? QUIPS.right : QUIPS.wrong));
    kit.announce(shout + ' ' + amount + '. ' + (win ? '' : 'The answer was ' + LETTERS[q.correct] + '. ') + text(q.explain));
    kit.disable(false);
    App.resize();
  }

  function rating(pct) {
    if (pct >= 90) return 'Security champion';
    if (pct >= 70) return 'Well prepared';
    if (pct >= 50) return 'Getting there';
    return 'Time to brush up';
  }

  function results() {
    state = 'done';
    stopClock();
    var n = qs.length, pct = Math.round(right * 100 / n);
    var weak = Object.keys(cats).filter(function (k) { return cats[k].right < cats[k].n; })
      .sort(function (a, b) { return cats[a].right / cats[a].n - cats[b].right / cats[b].n; }).slice(0, 3);
    var tier = cash < 0 ? 'broke' : pct >= 90 ? 'top' : pct >= 70 ? 'good' : pct >= 50 ? 'mid' : 'low';
    clear(board);
    kit.stat('q', n + '/' + n);
    board.appendChild(h('div', { class: 'big' + (cash < 0 ? ' neg' : ''), text: money(cash) }));
    board.appendChild(h('p', { class: 'rate', text: rating(pct) + ': ' + right + ' of ' + n + ' right' }));
    add(board, host(pick(QUIPS.close[tier])));
    board.appendChild(h('p', { class: 'weak', text: weak.length ? 'Brush up on: ' + weak.join(', ') : 'No weak spots in this round.' }));
    if (missed.length) {
      var list = h('ul', { class: 'miss' });
      missed.forEach(function (q) {
        list.appendChild(h('li', {},
          h('div', { class: 'q', text: text(q.stem) }),
          h('div', { class: 'a', text: 'Answer: ' + text(q.options[q.correct]) }),
          q.explain ? h('div', { text: text(q.explain) }) : null));
      });
      board.appendChild(list);
    }
    board.appendChild(h('p', { class: 'none', text: 'Nothing is stored. This quiz is practice, not a training record.' }));
    kit.label('Play again');
    kit.disable(false);
    kit.announce('Quiz finished. You ended with ' + money(cash) + ' and got ' + right + ' of ' + n + ' right. ' + rating(pct) + '.');
    kit.report('The user finished a Cyber Trivia security awareness quiz show with ' + money(cash) + ': ' + right + ' of ' + n + ' correct' +
      (weak.length ? ', weakest topics: ' + weak.join(', ') : '') + '.',
      { game: 'cyber-trivia', cash: cash, correct: right, count: n, level: data.level || 'any', weakCategories: weak, bankVersion: data.bankVersion || '' });
    App.resize();
  }

  function restart() {
    idx = 0; cash = 0; right = 0; missed = []; cats = {};
    show();
  }

  function next() {
    if (state === 'intro') { restart(); return; }
    if (state === 'done') { restart(); return; }
    if (!answered) return;
    idx++;
    if (idx >= qs.length) results(); else show();
  }

  var reserveTimer = null;
  function relayout() { clearTimeout(reserveTimer); reserveTimer = setTimeout(reserve, 150); }

  function leave() {
    window.removeEventListener('focus', wake);
    window.removeEventListener('resize', relayout);
    document.removeEventListener('pointerdown', wake);
    clearTimeout(reserveTimer);
    stopClock();
  }

  window.addEventListener('focus', wake);
  window.addEventListener('resize', relayout);
  document.addEventListener('pointerdown', wake);
  App.onMode(relayout);
  kit.onPrimary(next);
  kit.onAway(pauseClock);
  kit.onStop(leave);
  kit.onKey(function (key, onBoard) {
    wake();
    if (state === 'q' && !answered && key.length === 1) {
      var n = 'abcd'.indexOf(key.toLowerCase());
      if (n < 0 && key >= '1' && key <= '4') n = Number(key) - 1;
      if (n >= 0) { settle(n); return true; }
    }
    if (onBoard && (key === 'Enter' || key === ' ') && (state !== 'q' || answered)) { next(); return true; }
    return false;
  });
  intro();
  reserve();
}

start('Cyber Trivia', {
  bankVersion: 'sample', level: 'any', timed: true, tone: 'cheeky',
  questions: [
    { category: 'phishing', categoryName: 'Phishing', level: 'basic', stem: 'An email from IT says your mailbox is full and asks you to click a link. What should you do?',
      options: ['Click the link and sign in quickly', 'Do not click it; report the email', 'Reply to ask if it is real', 'Forward it to coworkers'], correct: 1,
      explain: 'Urgent warnings that push you to click are a classic phishing tactic.', action: 'Report it to your security team, then delete it.' },
    { category: 'passwords', categoryName: 'Passwords and MFA', level: 'basic', stem: 'Which is the best way to create a strong password?',
      options: ['A short password with many symbols', 'Your name and birth year', 'A long passphrase of unrelated words', 'A word with letters swapped for numbers'], correct: 2,
      explain: 'Length matters more than complexity.', action: 'Use a unique passphrase for each account.' },
    { category: 'devices', categoryName: 'Devices', level: 'basic', stem: 'You find a USB drive in the parking lot. What should you do?',
      options: ['Plug it in to see whose it is', 'Plug it in on a spare laptop', 'Give it to your security team', 'Plug it in at home'], correct: 2,
      explain: 'Attackers leave drives to be found.', action: 'Hand it in and let the security team check it.' }
  ]
}, playTrivia, ['inline', 'fullscreen']);
";

    // ── Word Guess ──────────────────────────────────────────────────────
    //
    //    The lists are built by tools/build-word-lists.py from ENABLE (public domain), ranked with
    //    Peter Norvig's word counts. Run the script to refresh them; do not edit between the markers.

    private const string WordLists = @"
// BEGIN WORD LISTS
var WORD_ANSWERS = 'abbey abide about above abuse actor acute adapt admit adobe adopt adult after again agent aging agree ahead alarm album alert alias alien align alike alive alley allow alloy alone along aloud alpha alter amber amend amino among ample angel anger angle angry anime ankle annex apart apple apply arbor arena argue arise armor array arrow aside aspen assay asset atlas attic audio audit avail avoid awake award aware awful bacon badge badly baker baron basic basis batch baton beach beard beast began begin begun beige being belle belly below bench berry biker bingo birth black blade blame blank blast blend bless blind blink bliss block blond blood bloom blown bluff blunt board bonus boost booth borne bound boxer brain brake brand brass brave bread break brick bride brief bring broad broke brook brown brush buddy build built bunch bunny burnt burst buyer cabin cable cache camel canal candy canoe canon carat cargo carol carry catch cause cease cedar chain chair charm chart chase cheap cheat check cheer chess chest chick chief child chili chill choir chose chuck cigar circa civic civil claim clamp clash class clean clear clerk click cliff climb clock clone close cloth cloud coach coast cobra cocoa colon color combo comic condo coral corps couch cough could count coupe court cover crack craft crane crash crazy cream creed creek crest cried crime crisp cross crowd crown crude cruel crush cubic curry curse curve cycle daddy daily dairy daisy dance dealt death debit debug debut decay decor delay delta demon denim dense depot depth derby devil diary digit dinar dirty disco dodge doing donor doubt dozen draft drain drake drama drawn dream dress dried drift drill drink drive drove drunk dryer dummy dying eager eagle early earth eaten ebony eight elbow elder elect elite empty enemy enjoy enter entry equal equip error essay event every exact excel exist extra fairy faith false fancy fatal fatty fault favor feast fence ferry fever fewer fiber fibre field fifth fifty fight final first flame flash fleet flesh flint float flood floor flora flour fluid flush flute flyer focal focus force forge forth forty forum found frame frank fraud freak fresh fried front frost fruit fully funky funny fuzzy gamma gauge genre genus ghost giant given glass globe glory gloss glove gnome going goose grace grade grain grand grant grape graph grasp grass grave great green grief grill gross group grove grown guard guess guest guide guild guilt habit hairy handy happy hardy harry harsh hatch haven heard heart heath heavy hedge hello hence hobby holly honey honor horse hotel house human humor hurry hyper ideal idiot image imply index indie inlet inner input intro issue ivory jelly jewel joint judge juice juicy jumbo karma kitty knife knock known label labor laden lance large laser later latex laugh layer learn lease least leave legal lemma lemon level lever light limit linen liner liver lobby local lodge logic loose lotus lover lower loyal lucky lunar lunch lying lyric macro magic major maker mambo mania manor maple march marry marsh mason match maxim maybe mayor meant medal media menus mercy merge merit merry metal meter metro micro midst might minor minus mixer model modem molly money month moody moose moral motel motor mount mouse mouth movie music nasty naval nerve never newer newly niche night ninja ninth noble noise noisy north notch novel nurse nylon oasis occur ocean offer often older olive omega onion onset opera optic orbit order organ other ought ounce outer owner oxide ozone paint panda panel panic paper parse party pasta paste patch patio pause peace peach pearl pedal penny petty phase phone photo piano piece pilot piper pitch pixel pizza place plain plane plant plate plaza plump plush point poker polar porch pouch pound power press price pride prime print prior prize probe promo proof proud prove proxy puffy pulse punch pupil puppy purse quake queen query quest queue quick quiet quilt quite quota quote racer radar radio raise rally range rapid ratio raven razor reach react ready realm rebel refer rehab reign relax relay remix renal renew reply reset resin retro reuse rider ridge rifle right rigid rival river roast robin robot rocky rodeo rogue rouge rough round route rover royal rugby rural sadly safer saint salad salon salsa samba sandy satin sauce sauna saver scale scarf scary scene scent scoop scope score scout scrap screw scuba sedan sense serum serve setup seven sewer shade shaft shake shall shame shape share shark sharp sheep sheer sheet shelf shell shift shine shiny shirt shock shoot shore short shout shown sight sigma silly since sixth sixty skate skill skirt skull slate sleep slice slide slope small smart smash smell smile smoke snack snake solar solid solve sorry sound south space spare spark speak speed spell spend spent spice spicy spike spill spine spite split spoke spoon sport spray squad squid stack staff stage stain stake stamp stand stark start state steak steal steam steel steep stein stern stick stiff still stock stone stood store storm story stove strap straw strip stuck study stuff style suede sugar suite sunny super surge sushi sweat sweep sweet swift swing sword table taken tarot taste tasty teach teddy teeth tempo tenth thank theft their theme there these theta thick thing think third those three threw throw thumb tiger tight timer titan title toast today token toner tooth topic torch total touch tough towel tower toxic trace track tract trade trail train trash treat trend trial tribe trick tried trout truck truly trunk trust truth tumor tuner turbo tutor twice twist ultra uncle under union unity until upper upset urban usage usher using usual valid value valve vapor vault venue verse video villa vinyl viral virus visit vista vital vivid vocal voice voter wagon waist waste watch water wedge weigh weird whale wheat wheel where which while white whole whose wider widow width witch woman women woody world worry worse worst worth would wound woven wrist write wrong wrote yacht yeast yield young youth'.split(' ');
var WORD_GUESSES = 'aargh abaca aback abate abbas abbot abhor ables abode abort abuts abuzz abyss acari ached aches acids acorn acres acrid acted actin adage added adder adept adieu adios adobo adore adorn aedes aegis aeons aerie affix afire afoot afore afoul agape agate agave agers aggie aggro agile agios aglow agony agora ahold aided aider aides aimed aimer aioli aired aisle alamo aland alder aleph algae algal algor alibi aline alist aliya alkyd alkyl allay allee allot allyl aloft aloha aloof altar altho altos alums alway amass amaze ambit amble ambos amici amide amiga amigo amine amiss amity amour amply amuse angst anima anion anise annal annas annoy annul anode antes antic antsy anvil aorta apace aphid aphis apnea appel apres apron apter aptly arcus ardor areal areas areca arete argon argus arias ariel arles armed aroma arose arras arses arson artsy asana ascot ashen ashes asked asker askew asper asses aster atman atoll atoms atone atria attar auger aught augur aunts aunty aural auras auric aurum autos auxin avant avast avers avert avian avion aviso await awash awoke axial axiom axion axles axons azide azure babel babes backs bagel baggy bahts bails baits baked bakes baldy baled baler bales balls bally balms balmy balsa banal banco bands bandy banes bangs banjo banks banns barbe barbs bards bared barer bares barge barks barmy barns barre basal based bases basil basin bassi basso baste bates bathe baths batik batts batty bawdy bayou bazar beads beady beaks beams beano beans bears beats beaut beaux bebop becks beech beefs beefy beeps beers beery beets begat beget begum belay belch belga belie bells belts bends bendy benes benni benny beret berms berth beryl beset bests betas betel beton betta bevel bezel bible biddy bidet bight bigot bijou bikes bilbo bilge bills billy bimbo bindi binds binge biome biota biped birch birds birks bison bitch biter bites bitsy bitty blain bland blase blaze bleak bleat bleed bleep blest blimp blips blitz bloat blobs blocs bloke blots blows blued bluer blues bluey blume blurb blurs blurt blush boars boast boats bobby bocce bodes bogan bogey boggy bogie bogle bogus boils boing boite boles bolts bolus bombe bombs bonds boned boner bones boney bongo bongs bonne bonny boobs booby booed books booms boots booty booze boozy boral borax bored borer bores boric boron bosom boson bossy bosun botch botel botts bough boule bourg bourn bouse bouts bowed bowel bower bowls boxed boxes brace brach brads brags braid brant brash brats brava bravo brawl brawn bream brede breed brees brent breve brews briar bribe brier brill brine brink brisk brits britt brock broil brome bromo bronc brood broom broth brows bruin brunt brute bucks budge buffs buffy buggy bugle bulbs bulge bulky bulla bulls bully bumps bumpy bundt bunko bunks buoys burbs burgh burke burly burns burro burrs burry bursa busby buses bushy busts busty butch buteo butte butts butyl buxom bylaw bytes byway cabal cacao cacti caddy cades cadet cadre cafes caged cages cairn cajon caked cakes calif calla calls calms calyx camas cameo campi campo camps campy caned canes canna canny canst canto canty caped caper capes capon capos caput carbo carbs cards cared carer cares caret carex carle carls carob carps carrs carte carts carve casas cased cases casks caste casts cater cates catty caulk caved caves cecum ceded ceiba celeb cella cello cells celts cento cents ceres cesta chads chafe chaff chalk champ chang chant chaos chaps chapt chard chars chasm chats cheek cheep chefs chela chemo chert chevy chews chewy chiao chico chics chile chime chimp china chine chink chino chins chips chiro chirp chive chock choke chomp chops chord chore chubs chump chums chunk churn chute cider cilia cinch cisco cissy cited cites clack clade clams clang clank clans claps claro clary clasp clast clave claws clays cleat clefs cleft clews clift cline cling clink clips cloak clogs clots clout clove clown cloze clubs cluck clued clues clump clung coals coats cobbs cobia coble cocks cocky cocos codec coded coden coder codes codex codon coeds coffs coils coins cokes colas colds coles colic colin colts comal comas combe combs comer comes comet comfy comix comma compo comps comte conch cones coney conga congo conic conte conto conus cooks cools coons coops coots copal coped copes copra copse corby cords cored corer cores corgi coria corks corky corns corny corse coset costa costs cotes cotta coups coven coves covet covey cowed cower crabs crags crake cramp crank crape craps crass crate crave crawl craze creak credo creel creep creme crepe crept cress crews cribs crick crier cries crimp croak crock crocs croft crone crony crook crops crore croup crows cruet crumb crump cruse crust crypt cubby cubed cubes cubit cuddy cuffs culex cully culpa cults cumin cunts cupid cuppa curbs curds cured cures curia curie curio curls curly curvy cushy cusps cuter cutie cutty cyano cyclo cynic cysts czars dados daffy dales dally daman dames dandy danio dared dares darts dated dater dates datos datum dawns dazed deals deans dears deary debts debye decaf decal decks decoy decry deeds deems deeps deers defer degas deign deism deity delft delis dells delve demos denes dents deoxy derma derry desks deter detox deuce devas devel devon dewan dewar dials diced dices dicey dicks dicky dicot dicta didst diene diets dikes dildo dilly dimer dimes dimly dined diner dines dingo dings dingy dinky diode dirge dirks disci discs disks ditch ditto ditty divan divas dived diver dives divot diwan dixit dizzy djinn dobby dobie dobra docks dodgy doers doeth doggy dogma doily dolce dolci doled doles dolls dolly dolor domed domes donee dongs donna donne donut dooms doors doozy doped dopey dorks dorky dorms dosed doses dotty douce dough douse doves dowdy dowel dower downs downy dowry doyen dozed dozer drags drams drank drape drawl draws dread dreck drees dregs drier dries drips droit droll drone drool droop drops dross drown drugs druid drums dryad dryly duals ducal duces duchy ducks ducky ducts dudes duels duets duffs dukes dumas dumps dumpy dunce dunes dungy dunks duomo duped duper dupes dural duras dures durst durum dusky dusts dusty dutch duvet dwarf dwell dwelt dyads dyers dykes eared earls earns eased easel eases eater eaves echos eclat edema edged edger edges edict edits eerie egger egret eider eidos eject eland elegy elfin elope elude elves embed ember emcee emery emits emmer emmet emote enact ended ender endow enema ennui enrol ensue envoi envoy eosin epics epoch epoxy erase erect ergot erica erode erred erupt esker esses ester ether ethic ethos ethyl etude euros evade evens evert evict evils evite evoke exalt exams execs exert exile exits exons expat expel expos extol exude exult fable faced faces facet facia facts faded fader fades faery fagin fails faint fairs faked fakes falls famed fames fangs fanny fanon farad farce fared fares farms farts fasts fated fates fatso fatwa fauna fauns faves fawns faxed faxes fazed fears feats fecal feces feeds feels feign feint feist fella fells felly felon felts femme femur feral feria fermi ferns ferny fetal fetch feted fetes fetid fetus feuds fiche ficus fiend fiery filed filer files filet fille fills filly films filmy filth finch finds fined finer fines finis finny fiord fired fires firms firth fishy fists fitch fiver fives fixed fixer fixes fixit fizzy fjord flack flags flail flair flake flaky flank flaps flare flask flats flaws fleas fleck fleer flees flick flics flied flier flies fling flips flirt flite flock flops floss flout flown flows flues fluff fluke flume flung flunk fluor flyby foals foams foamy foggy foils folds folia folio folks folky folly fonds fonts foods fools foots footy foram foray forbs fords fores forgo forks forme forms forte forts fossa fosse fouls fount fours fovea fowls foxes foyer frags frail franc fraps freed freer frees frere frets friar fries frill frise frisk frith frits fritz frizz frock frogs frond frosh froth frown froze fryer fucks fudge fuels fugue fumed fumes funds fungi furan furor furry fused fuses fussy futon gabby gable gadis gaffe gages gaily gains galas galax galea gales galls gamba gamed gamer games gamin gamut gangs ganja gapes garni garth gases gasps gated gates gator gaudy gault gaunt gauss gauze gavel gazed gazer gazes gears gecko geeks geeky geese gemma genes genet genie genii genoa genom gents geode geoid germs gesso ghast ghats ghazi ghoul ghyll giddy gifts gigas gills gilly gilts gimme ginny gipsy girls girly girth giver gives gizmo glace glade gland glans glare glaze gleam glean glebe glens glial glide glint glitz gloat globs gloom glows glued glues gluon glyph gnats goals goats godly goers golds golem golly gonad goner gongs gonzo goods goody gooey goofs goofy gooks goons gored gores gorge gorse gouge gourd gowan gowns graal grabs grads graft grail grama grams grate gravy grays graze grebe greed greek greet greys grids griff grime grimy grind grins griot gripe grips grist grits groan groat groin groom grope grosz grout growl grows grubs gruel gruff grump grunt guano guava guile guise gulag gulch gules gulls gully gumbo gummy gunny guppy gurus gusto gusts gusty gutsy gutta guyot gypsy gyros gyrus hacks hades hadji hadst hafiz haiku hails hairs hakim hales hallo halls halos halts halve hames hammy hamza hance hands hangs hanks hanky hansa hants haply hards harem hares harms harps harpy harts haste hasty hated hater hates hauls haunt haute haver haves havoc hawks hazel heads heady heals heaps hears heats heave heels hefty heigh heirs heist helio helix hells helms helps henna henry herbs herby herds heres herma heron heros hertz hexes hicks hider hides highs hight hiked hiker hikes hills hilly hinds hinge hints hippo hippy hired hirer hires hissy hitch hives hoard hoary hobos hocks hocus hogan hoist hokey holds holed holes holey holla holts homed homer homes homey homos honda honed hones honky hooch hoods hoody hoofs hooks hooky hoops hoots hoped hopes hoppy horas horde horns horny horst hosed hosen hoses hosta hosts hotly hound hours hovel hover howdy howes howls hoyas hoyle hubby huffy hullo hulls humic humid humps humus hunch hunks hunky hunts hurst hurts husks husky hussy hutch hydra hydro hyena hymen hymns hyped icing icons ideas idiom idled idler idols idyll igloo ileal ileum iliac iliad ilium imago imams imbue impel inane incur inept inert infer infix infos infra ingle ingot inked inker inlay inset inter inure ionic irate irked irons irony isles islet itchy items jacks jacky jaded jades jager jails jakes jammy janes japan jaunt jawed jazzy jeans jebel jeeps jeers jenny jerks jerky jerry jesse jetty jiffy jihad jimmy jingo jinks jnana jocks johns joins joist joked joker jokes jolly jolts jones joram joule joust judas jukes julep jumps jumpy junco junky junta junto juror kabob kafir kalam kamik kanji kapok kappa kaput karat karns karoo karst karts kasha kauri kayak kazoo kebab keels keeps kelly kemps kendo kerns kerry ketch keyed khaki kicks kiddo kiddy kilim kills kilns kilos kilts kinds kings kinks kinky kiosk kissy kites kiwis klong kloof kluge klutz knack knave knead kneed kneel knees knell knelt knits knobs knoll knots knows koala kohls kooks kooky kraft kraut krill krona krone kroon kudos kudzu kugel kurta kylix kyrie labia laced laces lacey lacks lader ladle lagan lager laird lairs laity laker lakes lakhs lamas lambs lamer lamia lamps lanai lands lanes lanky lapel lapin lapis lapse larch largo larks larva lasso lasts latch lated lathe latte lauds laura lavas laver lawns layed layup lazar leach leads leafs leafy leaks leaky leans leant leaps leapt leary leash leben ledge leech leeks leery lefty leger leggy legit leman lemur lends lense lento leone leper levee levin lewis lexis liana liane liang liars libel liber libra libri licht licks lidar liege liens lieve lifer lifts liger liked liken likes lilac liman limba limbo limbs limes limey limos linac lindy lined lines lingo links linky lions lipid liras lisle lists litas liter lithe litho litre lived liven lives livid livre llama llano loach loads loamy loans loath lobed lobes lobos lochs locks locos locum locus loden loess lofts lofty logan logos loins lolly loner longe longs looks looms loons loony loops loopy loper lopes loral loran lords loris lorry loser loses lossy lotos lotte lotto lough louie louis loupe louse lousy loved loves lowed lowes lowly lubes lucid lucre luger lumen lumps lumpy lunas lunes lunge lungs lupin lupus lurch lured lures lurid lurks lusts lusty lutea lutes lyase lymph lynch lysed lysis lytic macaw mache macho macks macon madam madly madre mafia mafic mages magma magus maids maile mails mains maize makes males malic malls malts malty mamas mamba mamie mamma mammy manas manes mange mango manic manly manna manos manse manta manus mares marge maria marks marly marts maser masks massa masse massy masts mated mater mates matey maths matin matte matts mauve maven mavis maxis mayan mazer mazes meads meals mealy means meany meats meaty mecca medic meets melds melee melon melts memos mensa merde merle mesas mesic meson messy meted metis metre mezzo micra midge midis miffy mikes milch miler miles milks milky mille mills milos mimeo mimes mimic minas mince minds mined miner mines minim minis minke mints minty mired mirth mirza miser mises missy mists misty miter mites mitre mitts mixed mixes moans moats mocha mocks modal modes modus mogul moira moire moist molar molds moldy moles molto momma mommy monad monde mondo mongo monks monos monte mooch moods moons moors moots moped moray morel mores moron morph morro morse morts mosso mossy motes motet moths motif motte motto mould moult mound mourn moved mover moves mowed mower moxie mucin mucky mucus muddy muffs mufti muggs muggy mulch mules mulls mummy mumps munch mungo muons mural mures murky murry mused muses mushy musky musty mutch muted mutes mutts muzzy myrrh myths nacho nadir nails naira naive naked named namer names nance nancy nanny nappy narco nasal natal natch nates natty navel naves nazis nears neath necks needs needy negus neigh nelly neons nerds nerdy nests netop neves nevus newel newts nexus nicad nicer nicks nicol nidal niece nieve nifty nihil nines nippy nisus nites nitro nitty nixed nixes nobby nobly nodal noddy nodes noirs nomad nomen nomos nonce nooks noose norms nosed noses nosey noted noter notes nouns novae novas nubia nudes nudge nudie nuked nukes nulls nutty nyala nymph oases oaten oaths obese obeys obits objet oboes ochre octal octet octyl oddly odeon odors odour offal ogres ohmic oiled oiler okays olden oldie oleic ology ombre omens omits oomph oozed oozes opals opens opine oping opium opted orals orang orcas oriel orris ortho ostia otter ousts outdo outed outre ovals ovary ovate ovens overs overt ovine ovoid owing owned oxbow paced pacer paces pacha packs pacts paddy padre paean pagan paged pager pages pails pains pairs paisa paled paler pales pally palms palsy pampa panes pangs panne pansy panto pants panty papal papas pappy paras pared pares paris parka parks parle parry parts paseo pasha passe pasts pasty pater pates paths patsy patty pavan paved paver paves pawns payed payee payer payor peaks peals pears peart pease peavy pecan pecks pedro peeks peels peeps peers peeve pekin pekoe pelts penal pence penes penis penna penne peony peppy perch perdu peril perks perky perms perry perse pesky pesos pesto pests petal peter petit phage phlox phono phony phots phyla picks picky picot piers pieta piety piggy pikes pilaf pilar piled piles pills pimps pinas pinch pines piney pingo pings pinko pinks pinky pinon pinot pinto pints pinup pions pious piped pipes pipet pipit pique pisco pisos piste pithy pivot pixie plage plaid plait plank plans plats playa plays plead pleas pleat plena plied plier plies plink plonk plots plows pluck plugs plumb plume plums plunk poach pocky poems poets poise poked pokes pokey poles polio polis polka polls polos polyp polys ponce ponds pooch pools poons poops popes poppa poppy pores porgy porky porno porns ports posed poser poses posit posse posts potty pours prams prank prawn prays preen preps presa prest preys prick pried prima primi primo prion prise prism priss privy prods profs progs proms prone prong props prose prost prowl prude prune psalm psych pubes pubic pucks pudgy puffs pukka pulls pulps pumas pumps punks punky punto punts pupae puree purer purge purrs pushy pussy putts putty pygmy pylon quack quads quail quale quant quark quart quash quasi quays queer quell quill quint quips quire quirk quits quoth rabat rabbi rabid raced races racks radii radix radon rafts ragas raged rages raids rails rains rainy rajah raked rakes ralph ramie ramps ramus rance ranch rands randy ranks rants raped raper rapes rarer rares raspy rated rater rates ratty raved ravel raver raves ravin rayed rayon razed razer reads reals reams reaps rears rebar rebbe rebus rebut recap recon recta recto recur recut redan redes redid redox redux reeds reedy reefs reeks reels reeve refit regal reins relic reman remap remit renin rents repay repel repos repro rerun resid rests retry revel revue rheum rhino rhomb rhyme rials ribes rices ricin ricks rides riffs rifts rigor riled riles riley rimes rinds rings rinks rinse rioja riots ripen riper risen riser rises rishi risks risky rites ritzy riven rives rivet riyal roach roads roams roars robed robes rocks roger roles rolls roman romeo rondo roofs rooks rooms roomy roost roots roped roper ropes roque roses rosin rotor rouen rouse routh rowan rowdy rowed rowen rower ruble rubus ruddy ruder ruins ruled ruler rules rumba rumen rummy rumor rumps runes rungs runic runny rupee rusty saber sabes sabin sable sabot sabra sabre sacks sacra sadhu safes sagas sager sages saggy sahib sails saith sakes sales sally salts salty salve salvo sambo sands saner sangh santo sappy saran saree sarge sarin saris sassy satay sated sates satis satyr saucy sault saute saved saves savin savor savoy savvy sawed saxes sayer sayid scabs scald scalp scaly scamp scams scans scant scape scare scarp scars scart scaup scena scion scoff scold scone scoot scorn scots scour scowl scram scree scrim scrip scrub scrum scuff scull seals seams sears seats sebum sects seder sedge sedum seeds seedy seeks seely seems seeps seers segni segno segue seine seize selah selle sells selva semen semis sends senna senor sensa sepia septa serfs serge serif servo setae seton sever sewed sexed sexes shack shady shaky shale shalt shams shank shard shaul shave shawl shawn shaws shays sheaf shear sheds sheen sheik shied shiel shill shims shins ships shire shirk shits shiva shive shoal shoes shoji shone shook shool shops shorn shots shove shows showy shred shrew shrub shrug shuck shuns shunt shute shuts shyly sibyl sicko sided sides siege sieve sighs sigil signs sikes silex silks silky sills silos silty silva sines sinew singe sings sinks sinus sired siren sires sisal sissy sitar sited sites situs siver sixes sized sizer sizes skeen skeet skein skene skews skids skied skier skies skiff skimp skink skins skint skips skits skunk slabs slack slags slain slams slang slant slaps slash slats slave slays sleds sleek sleet slept slick slime slims slimy sling slink slips slits sloop sloth slots slows slugs slump slums slung slurp slurs slush sluts slyly smack smear smelt smirk smite smith smock smoky smolt smote smuts snafu snags snail snaps snare snark snarl sneak sneer snell snide sniff snipe snips snobs snood snook snoop snore snort snout snows snowy snubs snuck snuff soaks soaps soapy soars sober socks sodas sodom sofas softs soggy soils sokol soldi soled soles solon solos solum solus sonar sonde sones songs sonic sonny sooth sooty sophy sorel sores sorts souls soups soupy sours sowed sower soyuz spade spake spall spank spans spars spasm spate spats spawn spear speck specs speer spelt sperm spews spica spied spiel spier spies spiff spiky spilt spins spiny spire spits spitz splat splay spode spoil spoof spook spool spoor spore spots spout spree sprig sprit sprue spuds spunk spurn spurs spurt squat squaw squib stabs stade stags staid stair stale stalk stall stang stank staph stare stars stash stats stave stays stead steed steer stele stems steno steps stews stich stile stilt sting stink stint stipe stirs stoic stoke stole stoma stomp stony stool stoop stops stork stour stout stray strep strum strut stubs studs stull stump stung stunk stuns stunt stupa sturt styli suave suber sucks sucre suing suits sulfa sulky sully sumac summa sumps sunna supra surah surer surfs surly sutra sutta swabs swags swain swale swami swamp swamy swank swans swaps sward swarm swart swash swath sways swear swede swell swept swill swims swine swipe swirl swish swiss swoon swoop swore sworn swung sykes sylva synch syncs synod synth syrup sysop tabby taber tabid tabla taboo tabor tacit tacks tacky tacos taffy taiga tails tains taint taker takes takin tales talks tally talon talus tamed tamer tames tammy tango tangy tanka tanks tansy tanto tapas taped taper tapes tapir tapis tardy tares tarps tarry tarts tasks tasse tatar tater tates tatty taunt taupe tawny taxed taxes taxis taxon taxus teams tears teary tease teats techy teens teeny telex telia telic tells telly telos tempi temps tempt tench tends tenet tenon tenor tense tents tepid terai terms terns terra terry terse tesla testa tests testy tetra texas texts thane thein therm thief thigh thine thins thiol thole thong thorn thorp thous throb thugs thuja thump thunk thyme tiara tibia tical ticks tidal tides tiers tikes tilak tilde tiled tiles tills tilts timed times timid tinea tines tinge tings tinny tints tippy tipsy tired tires titer tithe titre titty toads toddy toile toils tolls toman tombs tomes tommy tonal toned tones toney tonga tongs tonic tonne tools toons toots topaz topos toque torah torcs toric torii toros torso torte torts torus totem totes tours touts towed towns toxin toyed trait tramp trams trans traps trapt trave trawl trays tread treed trees treks tress triac triad trice trier tries trike trill trims trine trios tripe trips trite trois troll trona troop trope troth trots trove truce truer trump truss tryst tubal tubas tubby tuber tubes tucks tufts tulip tulle tummy tunas tuned tunes tunic turds turks turns tushy tusks tutti tuxes twain twang twats tweak tweed tween tweet twigs twill twine twins twirl twixt tyers tying tykes typed types typos tyres udder ulama ulcer ulnar umber umbra unary unban uncut undid undue unfit unify unite units unlit unmet unset untie unwed unzip updos upped urged urges urine users usurp usury uteri utile utter vagal vague vagus vales valet valor valse vamps vanda vanes vapid varia varna varus vases veena veers vegan veils veins venom vents verbs verge verso verts vertu verve vesta vests vetch vexed vials vibes vicar vices vichy views vigil vigor villi vinca vines vinos viola viper vireo vires visas vises visor vitae vixen voces vodka vogue voids voila voile voles volta volte volts vomit voted votes vouch vowed vowel vroom vrouw vulva vying wacko wacky waded wader wades wafer waged wager wages wahoo wails waits waive waked waker wakes wales walks walla walls wally waltz wands waned wanes wants wards wares warms warns warps warts warty washy wasps watts waugh waved waver waves waxed waxes weald wears weary weave webby weber wedel weeds weedy weeks weeps weirs welch welds wells welly welsh welts wench wests whack wharf whats whiff whigs whims whine whiny whips whirl whisk whist whizz whoop whore whorl whoso wicks widen wield wifes wigan wight wilco wilds wiles wills willy wilts wimps wimpy wince winch winds windy wines wings winks wiped wiper wipes wired wires wiser wisps wispy wists withe witty wives woken wolds wolfs wonks wonky woods wooed wools wooly woops woozy words wordy works worms wowed wraps wrath wreak wreck wrens wrest wring writs wroth wrung wryly wurst xenia xenon xeric xerox xylem yager yahoo yanks yards yarns yawns yearn years yells yerba yikes yodel yogic yogis yoked yokes yolks yours youse yucca yucky yummy zaire zazen zebra zeros zesty zilch zingy zippy zloty zonal zoned zones zooms'.split(' ');
// END WORD LISTS
";

    private const string WordCss = @"
html[data-mode=fullscreen] body{padding:24px;background:#2e1f12}
html[data-mode=fullscreen] .card.gword{max-width:520px;min-height:0;margin:0 auto;padding:20px;border-width:12px}
.gword{--ts:52px;--wood:linear-gradient(160deg,#ecce9c,#c99a5e);--ink:#3a2410;--ok:#4d9a45;--pr:#d6a021;--ab:#5a4030;max-width:420px;margin:8px auto;padding:14px;font-family:Georgia,'Times New Roman',serif;color:#f4ecd8;background:radial-gradient(120% 70% at 50% 0%,rgba(255,255,255,.14),transparent 60%),radial-gradient(2px 2px at 30% 40%,rgba(255,255,255,.05),transparent) 0 0/9px 9px,#1f4d37;border:10px solid #6b4423;border-radius:14px;box-shadow:inset 0 0 0 2px #3c2412,inset 0 4px 14px rgba(0,0,0,.55),0 4px 12px rgba(0,0,0,.45)}
html[data-mode=fullscreen] .gword{--ts:64px}
.gword.hc{--ok:#f0782e;--pr:#6fb3ee}
.gword .row.between{flex-wrap:wrap;gap:6px;align-items:center}
.gword h1{flex:1 1 100%;font-size:24px;line-height:30px;font-weight:700;letter-spacing:.2em;text-align:center;text-transform:uppercase;color:#f4e3b8;text-shadow:0 2px 0 rgba(0,0,0,.5)}
.gword .stats{flex:1 1 100%;justify-content:center;gap:10px}
.gword .stat{align-items:center;min-width:72px;padding:2px 10px;background:rgba(0,0,0,.28);border-radius:8px}
.gword .stat .cap{font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#cfdcc8}
.gword .stat strong{font-size:18px;color:#ffffff}
.gword .boardwrap{touch-action:auto}
.gword .boardwrap:focus-visible{outline:2px solid #f4e3b8;outline-offset:3px}
.wgrid{display:grid;gap:5px}
.wrow{display:grid;grid-template-columns:repeat(5,var(--ts));gap:5px}
.wrow.shake{animation:shake .4s}
.wrow.ans{margin-top:8px}
.tile{display:grid;place-items:center;width:var(--ts);height:var(--ts);font-size:calc(var(--ts) * .6);font-weight:700;text-transform:uppercase;color:var(--ink);background:rgba(0,0,0,.28);border:2px solid rgba(255,255,255,.14);border-radius:6px;--base:rgba(0,0,0,.28)}
.tile.fill{--base:#d9b27c;background:var(--wood);border-color:#8c5f2f;box-shadow:0 2px 0 #6b4423}
.tile.rev{color:#ffffff;border-color:transparent;box-shadow:0 2px 0 rgba(0,0,0,.45);background:var(--res);animation:flip .55s both;animation-delay:calc(var(--i) * .14s)}
.tile.g{--res:var(--ok)}
.tile.y{--res:var(--pr)}
.tile.x{--res:var(--ab)}
@keyframes flip{0%{transform:rotateX(0);background:var(--base)}49%{transform:rotateX(90deg);background:var(--base)}50%{transform:rotateX(90deg);background:var(--res)}100%{transform:rotateX(0);background:var(--res)}}
@keyframes shake{20%,60%{transform:translateX(-5px)}40%,80%{transform:translateX(5px)}}
@media (prefers-reduced-motion:reduce){.tile.rev,.wrow.shake{animation:none}}
.wkeys{display:grid;gap:6px;max-width:340px;margin:0 auto}
.wkrow{display:flex;gap:4px;justify-content:center}
.wkrow.pad{padding:0 5%}
.wkey{flex:1 1 0;min-width:0;height:44px;padding:0;font:inherit;font-size:14px;font-weight:700;text-transform:uppercase;color:var(--ink);background:var(--wood);border:0;border-radius:6px;box-shadow:0 2px 0 #7a5530;cursor:pointer}
.wkey.wide{flex:1.6 1 0;font-size:12px}
.wkey:active{transform:translateY(2px);box-shadow:none}
.wkey:focus-visible{outline:2px solid #ffffff;outline-offset:1px}
.wkey.g{color:#ffffff;background:var(--ok)}
.wkey.y{color:#ffffff;background:var(--pr)}
.wkey.x{color:#d9c9b6;background:var(--ab)}
.gword .msg{color:var(--fg1)}
.gword .btn{color:#3a2410;background:var(--wood);border:0;box-shadow:0 2px 0 #7a5530}
.gword .btn:hover:not(:disabled){background:linear-gradient(160deg,#f4d9ac,#d6a96c)}
.gword .actions{justify-content:center}
";

    private const string WordJs = @"
function scoreGuess(guess, answer) {
  var res = ['x', 'x', 'x', 'x', 'x'], left = {}, i;
  for (i = 0; i < 5; i++) {
    if (guess.charAt(i) === answer.charAt(i)) res[i] = 'g';
    else left[answer.charAt(i)] = (left[answer.charAt(i)] || 0) + 1;
  }
  for (i = 0; i < 5; i++) {
    var c = guess.charAt(i);
    if (res[i] !== 'g' && left[c] > 0) { res[i] = 'y'; left[c]--; }
  }
  return res;
}

function playWord(data) {
  var ROWS = clamp(data.maxGuesses, 4, 8, 6), hard = !!data.hardMode;
  var KEYS = ['qwertyuiop', 'asdfghjkl', 'zxcvbnm'];
  var WORDS = {};
  WORD_ANSWERS.forEach(function (w) { WORDS[w] = true; });
  WORD_GUESSES.forEach(function (w) { WORDS[w] = true; });
  var fixed = typeof data.word === 'string' && /^[a-zA-Z]{5}$/.test(data.word.trim()) ? data.word.trim().toLowerCase() : '';
  if (fixed) WORDS[fixed] = true;

  var grid = h('div', { class: 'wgrid', role: 'grid', 'aria-label': 'Guesses' });
  var keysEl = h('div', { class: 'wkeys' });
  var kit = gameShell({
    title: 'Word Guess', board: grid, cls: 'gword' + (data.highContrast ? ' hc' : ''),
    stats: [['guess', 'Guess'], ['wins', 'Won']], controls: keysEl
  });
  var answer, row, current, tiles, rows, history, keyState, done, wins = 0, ansRow;
  var keyEls = {};

  function build() {
    clear(grid);
    tiles = []; rows = [];
    for (var r = 0; r < ROWS; r++) {
      var line = h('div', { class: 'wrow', role: 'row', 'aria-label': 'Guess ' + (r + 1) }), cells = [];
      for (var c = 0; c < 5; c++) {
        var t = h('div', { class: 'tile', role: 'gridcell', 'aria-label': 'Empty', style: '--i:' + c });
        cells.push(t); line.appendChild(t);
      }
      tiles.push(cells); rows.push(line); grid.appendChild(line);
    }
    // Reserved so the card does not grow when a lost game reveals the word.
    ansRow = h('div', { class: 'wrow ans', role: 'row', 'aria-hidden': 'true', style: 'visibility:hidden' });
    for (var a = 0; a < 5; a++) ansRow.appendChild(h('div', { class: 'tile fill', role: 'gridcell', text: ' ' }));
    grid.appendChild(ansRow);
  }

  function buildKeys() {
    clear(keysEl);
    KEYS.forEach(function (letters, n) {
      var line = h('div', { class: 'wkrow' + (n === 1 ? ' pad' : '') });
      if (n === 2) line.appendChild(h('button', { class: 'wkey wide', type: 'button', text: 'Enter', onclick: submit }));
      letters.split('').forEach(function (ch) {
        var k = h('button', { class: 'wkey', type: 'button', text: ch, 'aria-label': ch.toUpperCase(), onclick: function () { typeLetter(ch); } });
        keyEls[ch] = k; line.appendChild(k);
      });
      if (n === 2) line.appendChild(h('button', { class: 'wkey wide', type: 'button', 'aria-label': 'Backspace', text: '\u232B', onclick: backspace }));
      keysEl.appendChild(line);
    });
  }

  function paintRow(r) {
    for (var c = 0; c < 5; c++) {
      var ch = current.charAt(c), t = tiles[r][c];
      t.textContent = ch;
      t.className = 'tile' + (ch ? ' fill' : '');
      t.setAttribute('aria-label', ch ? ch.toUpperCase() : 'Empty');
    }
  }

  function typeLetter(ch) {
    if (done || current.length >= 5) return;
    current += ch;
    paintRow(row);
  }

  function backspace() {
    if (done || !current.length) return;
    current = current.slice(0, -1);
    paintRow(row);
  }

  function hardError(word) {
    var need = {}, i;
    for (var n = 0; n < history.length; n++) {
      var g = history[n].guess, s = history[n].result, seen = {};
      for (i = 0; i < 5; i++) {
        if (s[i] === 'g' && word.charAt(i) !== g.charAt(i)) return 'Letter ' + (i + 1) + ' must be ' + g.charAt(i).toUpperCase() + '.';
        if (s[i] !== 'x') seen[g.charAt(i)] = (seen[g.charAt(i)] || 0) + 1;
      }
      for (var ch in seen) need[ch] = Math.max(need[ch] || 0, seen[ch]);
    }
    for (var letter in need) {
      var have = word.split(letter).length - 1;
      if (have < need[letter]) return 'The guess must contain ' + letter.toUpperCase() + '.';
    }
    return '';
  }

  function reject(text) {
    var line = rows[row];
    line.classList.remove('shake');
    void line.offsetWidth;
    line.classList.add('shake');
    kit.announce(text);
  }

  function submit() {
    if (done) return;
    if (current.length < 5) { reject('Not enough letters.'); return; }
    if (!WORDS[current]) { reject('Not in the word list.'); return; }
    if (hard) { var problem = hardError(current); if (problem) { reject(problem); return; } }
    var result = scoreGuess(current, answer), r = row, said = [];
    history.push({ guess: current, result: result });
    for (var c = 0; c < 5; c++) {
      var ch = current.charAt(c), t = tiles[r][c], word = result[c] === 'g' ? 'correct' : result[c] === 'y' ? 'in the word, wrong spot' : 'not in the word';
      t.className = 'tile rev ' + result[c];
      t.setAttribute('aria-label', ch.toUpperCase() + ', ' + word);
      said.push(ch.toUpperCase() + ' ' + word);
      var rank = { x: 1, y: 2, g: 3 };
      if (!keyState[ch] || rank[result[c]] > rank[keyState[ch]]) {
        keyState[ch] = result[c];
        keyEls[ch].className = 'wkey ' + result[c];
      }
    }
    kit.announce('Guess ' + (r + 1) + ': ' + said.join(', ') + '.');
    var won = current === answer;
    row++;
    current = '';
    kit.stat('guess', Math.min(row + (won || row >= ROWS ? 0 : 1), ROWS) + '/' + ROWS);
    if (won) finish(true);
    else if (row >= ROWS) finish(false);
  }

  function finish(won) {
    done = true;
    kit.label('New word');
    var d = { game: 'word-guess', won: won, guesses: row, maxGuesses: ROWS, hardMode: hard };
    if (won) {
      wins++;
      kit.stat('wins', wins);
      kit.announce('You got it in ' + row + (row === 1 ? ' guess.' : ' guesses.'));
      kit.report('The user solved a Word Guess puzzle in ' + row + ' of ' + ROWS + ' guesses.', d);
    } else {
      d.word = answer;
      ansRow.setAttribute('aria-label', 'The word was ' + answer.toUpperCase());
      ansRow.removeAttribute('aria-hidden');
      ansRow.style.visibility = 'visible';
      answer.split('').forEach(function (ch, n) { ansRow.children[n].textContent = ch; });
      kit.announce('Out of guesses. The word was ' + answer.toUpperCase() + '.');
      kit.report('The user did not solve a Word Guess puzzle. The word was ' + answer.toUpperCase() + '.', d);
    }
  }

  function begin() {
    answer = fixed || WORD_ANSWERS[rand(WORD_ANSWERS.length)];
    row = 0; current = ''; history = []; keyState = {}; done = false;
    build();
    Object.keys(keyEls).forEach(function (k) { keyEls[k].className = 'wkey'; });
    kit.label('New word');
    kit.stat('guess', '1/' + ROWS);
    kit.stat('wins', wins);
  }

  kit.onPrimary(begin);
  kit.onKey(function (key, onBoard, target) {
    if (key === 'Enter' || key === 'Backspace') {
      var other = target && target.tagName === 'BUTTON' && !target.classList.contains('wkey');
      if (other) return false;
      if (key === 'Enter') submit(); else backspace();
      return true;
    }
    if (key.length === 1 && /[a-zA-Z]/.test(key)) { typeLetter(key.toLowerCase()); return true; }
    return false;
  });
  buildKeys();
  begin();
}

start('Word Guess', { maxGuesses: 6 }, playWord, ['inline', 'fullscreen']);
";

    // ── Sudoku ──────────────────────────────────────────────────────────

    private const string SudokuCss = @"
html[data-mode=fullscreen] body{padding:24px;background:#d9d0b8}
html[data-mode=fullscreen] .card.gsudoku{max-width:640px;min-height:0;margin:0 auto;padding:24px}
.gsudoku{--cs:36px;--paper:#f7f1df;--ink:#1f1a14;--pen:#2b4a9b;--pencil:#6f6a5c;--line:#1f1a14;max-width:420px;margin:8px auto;padding:14px 14px 12px;font-family:Georgia,'Times New Roman',serif;color:var(--ink);background:var(--paper);border:1px solid #bdb08a;border-radius:2px;box-shadow:0 2px 8px rgba(0,0,0,.25)}
html[data-mode=fullscreen] .gsudoku{--cs:54px}
.gsudoku .row.between{flex-wrap:wrap;gap:4px;align-items:flex-end;padding-bottom:6px;border-bottom:3px double var(--ink)}
.gsudoku h1{flex:1 1 auto;font-size:26px;line-height:30px;font-weight:700;letter-spacing:.12em;text-transform:uppercase}
.gsudoku .stats{gap:14px}
.gsudoku .stat{min-width:48px;align-items:flex-end}
.gsudoku .stat .cap{font-size:10px;letter-spacing:.1em;text-transform:uppercase;color:var(--pencil)}
.gsudoku .stat strong{font-size:18px;color:var(--ink)}
.gsudoku .intro{min-height:16px;font-size:12px;letter-spacing:.06em;text-transform:uppercase;color:var(--pencil);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.gsudoku .boardwrap{touch-action:auto}
.gsudoku .boardwrap:focus-visible{outline:2px solid var(--pen);outline-offset:3px}
.sgrid{display:grid;grid-template-columns:repeat(9,var(--cs));width:max-content;margin:0 auto;border:3px solid var(--line);background:#fffdf5}
.scell{position:relative;display:flex;align-items:center;justify-content:center;width:var(--cs);height:var(--cs);font-size:calc(var(--cs) * .56);line-height:1;color:var(--pen);border-right:1px solid #b9ae92;border-bottom:1px solid #b9ae92;cursor:pointer;user-select:none}
.scell.br{border-right:2px solid var(--line)}
.scell.bb{border-bottom:2px solid var(--line)}
.scell:nth-child(9n){border-right:0}
.scell:nth-child(n+73){border-bottom:0}
.scell.giv{font-weight:700;color:var(--ink)}
.scell.hnt{font-style:italic;color:#7a3b8f}
.sgrid.shake{animation:ssh .3s}
@keyframes ssh{25%{transform:translateX(-4px)}75%{transform:translateX(4px)}}
@media (prefers-reduced-motion:reduce){.sgrid.shake{animation:none}}
.scell.peer{background:#f1e8cb}
.scell.same{background:#f4d98c}
.scell.sel{background:#ffe08a;box-shadow:inset 0 0 0 2px var(--pen)}
.scell.bad{color:#b3261e;background:#f9d9d5}
.scell.bad.sel{background:#f4b9b2}
.snotes{display:grid;grid-template-columns:repeat(3,1fr);width:100%;height:100%;padding:1px;font-size:calc(var(--cs) * .28);line-height:1;color:var(--pencil)}
.snotes span{display:flex;align-items:center;justify-content:center}
.ctl>*+*{margin-top:8px}
.spad{display:grid;grid-template-columns:repeat(9,1fr);gap:4px;max-width:calc(var(--cs) * 9 + 6px);margin:0 auto}
.stools{display:flex;flex-wrap:wrap;justify-content:center;gap:6px}
.gsudoku .btn{color:var(--ink);background:#efe6c9;border:1px solid #9e9272;border-radius:3px;font-family:inherit}
.gsudoku .btn:hover:not(:disabled){background:#e6dab5;border-color:#7d7152}
.spad .btn{min-width:0;height:40px;padding:0;font-size:20px;font-weight:700}
.spad .btn.done{color:#a89f86;background:#e2dcc6}
.stools .btn{min-width:0;height:30px;padding:0 10px;font-size:13px;font-weight:600}
.gsudoku .btn[aria-pressed=true]{color:#ffffff;background:var(--pen);border-color:var(--pen)}
.gsudoku .btn.primary{color:#ffffff;background:var(--ink);border-color:var(--ink)}
.gsudoku .btn.primary:hover:not(:disabled){background:#3a342b}
.gsudoku .btn:focus-visible{outline:2px solid var(--pen)}
.gsudoku .msg{color:#1f1a14;background:#fffdf5;border-color:#bdb08a}
.gsudoku .actions{justify-content:center}
";

    private const string SudokuJs = @"
var SU = { ROW: [], COL: [], BOX: [], PEERS: [] };
(function () {
  var i, a, b;
  for (i = 0; i < 81; i++) {
    var r = Math.floor(i / 9), c = i % 9;
    SU.ROW.push(r); SU.COL.push(c); SU.BOX.push(Math.floor(r / 3) * 3 + Math.floor(c / 3));
  }
  for (a = 0; a < 81; a++) {
    var p = [];
    for (b = 0; b < 81; b++) {
      if (a !== b && (SU.ROW[a] === SU.ROW[b] || SU.COL[a] === SU.COL[b] || SU.BOX[a] === SU.BOX[b])) p.push(b);
    }
    SU.PEERS.push(p);
  }
})();

function bitCount(m) { var n = 0; while (m) { n += m & 1; m >>= 1; } return n; }

// Counts solutions up to limit and returns the first one found. rng shuffles the digit order so a
// search on an empty grid yields a random full grid.
function sudokuSolve(grid, limit, rng) {
  var g = grid.slice(), rm = [], cm = [], bm = [], i;
  for (i = 0; i < 9; i++) { rm[i] = 0; cm[i] = 0; bm[i] = 0; }
  for (i = 0; i < 81; i++) {
    if (!g[i]) continue;
    var bit = 1 << (g[i] - 1);
    if ((rm[SU.ROW[i]] | cm[SU.COL[i]] | bm[SU.BOX[i]]) & bit) return { count: 0, solution: null };
    rm[SU.ROW[i]] |= bit; cm[SU.COL[i]] |= bit; bm[SU.BOX[i]] |= bit;
  }
  var count = 0, solution = null;
  function search() {
    var best = -1, bestMask = 0, bestN = 10, k;
    for (k = 0; k < 81; k++) {
      if (g[k]) continue;
      var m = ~(rm[SU.ROW[k]] | cm[SU.COL[k]] | bm[SU.BOX[k]]) & 511, n = bitCount(m);
      if (n < bestN) { best = k; bestMask = m; bestN = n; if (n <= 1) break; }
    }
    if (best < 0) { count++; if (!solution) solution = g.slice(); return count >= limit; }
    if (bestN === 0) return false;
    var digits = [], d;
    for (d = 1; d <= 9; d++) if (bestMask & (1 << (d - 1))) digits.push(d);
    if (rng) shuffle(digits, rng);
    for (var j = 0; j < digits.length; j++) {
      d = digits[j];
      var b = 1 << (d - 1), r = SU.ROW[best], c = SU.COL[best], x = SU.BOX[best];
      g[best] = d; rm[r] |= b; cm[c] |= b; bm[x] |= b;
      if (search()) return true;
      g[best] = 0; rm[r] &= ~b; cm[c] &= ~b; bm[x] &= ~b;
    }
    return false;
  }
  search();
  return { count: count, solution: solution };
}

var SU_GIVENS = { easy: 40, medium: 34, hard: 28, expert: 24 };

// A full grid, then cells removed in random order while the solution stays unique.
function sudokuMake(difficulty, seed) {
  var target = SU_GIVENS[difficulty] || 34, rng = mulberry32(seed), zeros = [], i;
  for (i = 0; i < 81; i++) zeros.push(0);
  var full = sudokuSolve(zeros, 1, rng).solution, puzzle = full.slice(), order = [], givens = 81, t0 = Date.now();
  for (i = 0; i < 81; i++) order.push(i);
  shuffle(order, rng);
  for (i = 0; i < 81 && givens > target && Date.now() - t0 < 250; i++) {
    var keep = puzzle[order[i]];
    puzzle[order[i]] = 0;
    if (sudokuSolve(puzzle, 2, null).count === 1) givens--; else puzzle[order[i]] = keep;
  }
  return { puzzle: puzzle, solution: full };
}

function sudokuParse(text) {
  if (typeof text !== 'string') return null;
  var s = text.replace(/\s+/g, '');
  if (!/^[0-9.]{81}$/.test(s)) return null;
  var grid = [];
  for (var i = 0; i < 81; i++) grid.push(s.charAt(i) === '.' ? 0 : Number(s.charAt(i)));
  return grid;
}

function playSudoku(data) {
  var difficulty = SU_GIVENS[data.difficulty] ? data.difficulty : 'medium';
  var seed = data.seed !== undefined && data.seed !== null ? clamp(data.seed, 0, 2147483647, 1) : rand(1000000);
  var customGrid = sudokuParse(data.puzzle);
  var cells = [], given, val, notes, sol, unique, sel = -1, notesMode = false, check = false;
  var undo, hints, done, label, started, custom = false, hinted = [], baseIntro = '';

  var grid = h('div', { class: 'sgrid', role: 'grid', 'aria-label': 'Sudoku board' });
  for (var i = 0; i < 81; i++) {
    var cell = h('div', { class: 'scell', role: 'gridcell' });
    cell.addEventListener('pointerdown', (function (n) { return function () { select(n); }; })(i));
    cells.push(cell);
    grid.appendChild(cell);
  }
  var pad = h('div', { class: 'spad' }), padBtns = [];
  for (var d = 1; d <= 9; d++) {
    var key = h('button', { class: 'btn', type: 'button', text: String(d), 'aria-label': 'Number ' + d, onclick: (function (n) { return function () { enter(n); }; })(d) });
    padBtns.push(key);
    pad.appendChild(key);
  }
  var notesBtn = h('button', { class: 'btn', type: 'button', 'aria-pressed': 'false', text: 'Notes: off', onclick: toggleNotes });
  var checkBtn = h('button', { class: 'btn', type: 'button', 'aria-pressed': 'false', text: 'Check: off', onclick: toggleCheck });
  var tools = h('div', { class: 'stools' }, notesBtn,
    h('button', { class: 'btn', type: 'button', text: 'Erase', onclick: erase }),
    h('button', { class: 'btn', type: 'button', text: 'Undo', onclick: undoMove }),
    h('button', { class: 'btn', type: 'button', text: 'Hint', onclick: hint }),
    checkBtn);
  var clock = makeTimer(function (s) { kit.stat('time', fmtTime(s)); });
  var kit = gameShell({
    title: 'Sudoku', board: grid, cls: 'gsudoku', intro: 'Sudoku',
    stats: [['time', 'Time'], ['left', 'Left']],
    controls: h('div', { class: 'ctl' }, pad, tools)
  });
  var introEl = document.querySelector('.gsudoku .intro');

  function pos(i) { return 'Row ' + (SU.ROW[i] + 1) + ', column ' + (SU.COL[i] + 1); }

  function conflict(i) {
    var p = SU.PEERS[i];
    for (var k = 0; k < p.length; k++) if (val[p[k]] === val[i]) return true;
    return false;
  }

  function paint(i) {
    var cell = cells[i], v = val[i], cls = 'scell';
    if (SU.COL[i] % 3 === 2 && SU.COL[i] < 8) cls += ' br';
    if (SU.ROW[i] % 3 === 2 && SU.ROW[i] < 8) cls += ' bb';
    if (given[i]) cls += ' giv';
    else if (v && hinted[i]) cls += ' hnt';
    if (i === sel) cls += ' sel';
    else if (sel >= 0 && (SU.ROW[i] === SU.ROW[sel] || SU.COL[i] === SU.COL[sel] || SU.BOX[i] === SU.BOX[sel])) cls += ' peer';
    if (sel >= 0 && i !== sel && v && v === val[sel]) cls += ' same';
    if (v && (conflict(i) || (check && unique && !given[i] && v !== sol[i]))) cls += ' bad';
    cell.className = cls;
    clear(cell);
    var text = pos(i) + ', ' + (v ? v + (given[i] ? ', given' : '') : 'empty');
    if (v) cell.textContent = String(v);
    else if (notes[i]) {
      var box = h('div', { class: 'snotes' }), shown = [];
      for (var n = 1; n <= 9; n++) {
        var on = notes[i] & (1 << (n - 1));
        box.appendChild(h('span', { text: on ? String(n) : '' }));
        if (on) shown.push(n);
      }
      cell.appendChild(box);
      text += ', notes ' + shown.join(' ');
    }
    cell.setAttribute('aria-label', text);
    cell.setAttribute('aria-selected', i === sel ? 'true' : 'false');
  }

  function paintAll() {
    var empty = 0, count = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0], i;
    for (i = 0; i < 81; i++) { paint(i); if (!val[i]) empty++; else count[val[i]]++; }
    for (i = 1; i <= 9; i++) padBtns[i - 1].className = 'btn' + (count[i] >= 9 ? ' done' : '');
    kit.stat('left', empty);
  }

  function select(i) {
    sel = i;
    paintAll();
  }

  function startClock() { if (!done && !started) { started = true; clock.start(); } }

  function shakeBoard() {
    grid.classList.remove('shake');
    void grid.offsetWidth;
    grid.classList.add('shake');
  }

  function apply(snap) {
    undo.push(snap);
    introEl.textContent = baseIntro;
    paintAll();
    finishCheck();
  }

  function enter(n) {
    if (done) return;
    if (sel < 0 || given[sel]) { shakeBoard(); return; }
    startClock();
    var snap = [[sel, val[sel], notes[sel]]], bit = 1 << (n - 1);
    if (notesMode) {
      if (val[sel]) { shakeBoard(); return; }
      notes[sel] ^= bit;
    } else {
      if (val[sel] === n) return;
      val[sel] = n; notes[sel] = 0; hinted[sel] = false;
      SU.PEERS[sel].forEach(function (p) {
        if (notes[p] & bit) { snap.push([p, val[p], notes[p]]); notes[p] &= ~bit; }
      });
    }
    apply(snap);
  }

  function erase() {
    if (done || sel < 0 || given[sel] || (!val[sel] && !notes[sel])) return;
    startClock();
    var snap = [[sel, val[sel], notes[sel]]];
    val[sel] = 0; notes[sel] = 0; hinted[sel] = false;
    apply(snap);
  }

  function undoMove() {
    if (done || !undo.length) return;
    undo.pop().forEach(function (e) {
      val[e[0]] = e[1]; notes[e[0]] = e[2];
      if (e[1] !== sol[e[0]]) hinted[e[0]] = false;
    });
    introEl.textContent = baseIntro;
    paintAll();
  }

  function toggleNotes() {
    notesMode = !notesMode;
    notesBtn.setAttribute('aria-pressed', String(notesMode));
    notesBtn.textContent = 'Notes: ' + (notesMode ? 'on' : 'off');
  }

  function toggleCheck() {
    check = !check;
    checkBtn.setAttribute('aria-pressed', String(check));
    checkBtn.textContent = 'Check: ' + (check ? 'on' : 'off');
    paintAll();
  }

  function candidates() {
    var cand = [];
    for (var i = 0; i < 81; i++) {
      var m = 0;
      if (!val[i]) {
        m = 511;
        SU.PEERS[i].forEach(function (p) { if (val[p]) m &= ~(1 << (val[p] - 1)); });
      }
      cand.push(m);
    }
    return cand;
  }

  // A hint fixes a wrong number, or fills the selected cell, or fills the most constrained cell.
  function hint() {
    if (done) return;
    var i, target = -1, best = 10, cand = candidates();
    for (i = 0; i < 81; i++) {
      if (val[i] && !given[i] && val[i] !== sol[i]) { target = i; break; }
    }
    if (target < 0 && sel >= 0 && !val[sel]) target = sel;
    if (target < 0) {
      for (i = 0; i < 81; i++) {
        if (!val[i] && bitCount(cand[i]) < best) { best = bitCount(cand[i]); target = i; }
      }
    }
    if (target < 0) return;
    startClock();
    hints++;
    var snap = [[target, val[target], notes[target]]], bit = 1 << (sol[target] - 1);
    val[target] = sol[target]; notes[target] = 0; hinted[target] = true;
    SU.PEERS[target].forEach(function (p) {
      if (notes[p] & bit) { snap.push([p, val[p], notes[p]]); notes[p] &= ~bit; }
    });
    sel = target;
    kit.announce(pos(target) + ' filled with ' + sol[target] + '.');
    apply(snap);
  }

  function finishCheck() {
    var i;
    for (i = 0; i < 81; i++) if (!val[i]) return;
    for (i = 0; i < 81; i++) if (conflict(i)) { introEl.textContent = 'Board full, but some numbers break the rules'; return; }
    if (unique) {
      for (i = 0; i < 81; i++) if (val[i] !== sol[i]) { introEl.textContent = 'Board full, but some numbers are wrong'; return; }
    }
    done = true;
    clock.stop();
    var secs = clock.seconds();
    introEl.textContent = 'Solved in ' + fmtTime(secs) + ' \u00B7 ' + (hints ? hints + (hints === 1 ? ' hint' : ' hints') : 'no hints');
    kit.announce('Puzzle solved in ' + fmtTime(secs) + '.');
    kit.report('The user solved a ' + label + ' Sudoku in ' + fmtTime(secs) + (hints ? ' using ' + hints + (hints === 1 ? ' hint' : ' hints') : ' without hints') + '.',
      { game: 'sudoku', solved: true, difficulty: label, seconds: secs, hints: hints });
  }

  function begin(fresh) {
    var made = null;
    custom = false;
    if (customGrid && !fresh) {
      var result = sudokuSolve(customGrid, 2, null);
      if (result.count > 0) { made = { puzzle: customGrid, solution: result.solution }; custom = true; }
    }
    if (!made) {
      if (fresh) seed = rand(1000000);
      made = sudokuMake(difficulty, seed);
    }
    given = made.puzzle.map(function (v) { return v !== 0; });
    val = made.puzzle.slice();
    sol = made.solution;
    unique = sudokuSolve(made.puzzle, 2, null).count === 1;
    notes = []; for (var i = 0; i < 81; i++) notes.push(0);
    undo = []; hints = 0; done = false; started = false; sel = -1;
    label = custom ? 'custom' : difficulty;
    hinted = [];
    clock.reset();
    baseIntro = custom ? 'Custom puzzle' : difficulty + ' \u00B7 puzzle ' + seed;
    introEl.textContent = baseIntro;
    kit.label('New puzzle');
    paintAll();
  }

  kit.onPrimary(function () { begin(true); });
  kit.onAway(function () { if (started && !done) { clock.stop(); started = false; } });
  kit.onStop(function () { clock.stop(); });
  kit.onKey(function (key, onBoard) {
    if (key.length === 1) {
      var n = '123456789'.indexOf(key);
      if (n >= 0) { enter(n + 1); return true; }
      var k = key.toLowerCase();
      if (k === '0') { erase(); return true; }
      if (k === 'n') { toggleNotes(); return true; }
      if (k === 'z') { undoMove(); return true; }
      if (k === 'h') { hint(); return true; }
    }
    if (key === 'Backspace' || key === 'Delete') { erase(); return true; }
    var move = { ArrowUp: -9, ArrowDown: 9, ArrowLeft: -1, ArrowRight: 1 }[key];
    if (move && onBoard) {
      var next = sel < 0 ? 0 : sel + move;
      if (next < 0 || next > 80 || (Math.abs(move) === 1 && SU.ROW[next] !== SU.ROW[sel])) next = sel < 0 ? 0 : sel;
      select(next);
      return true;
    }
    return false;
  });
  begin(false);
}

start('Sudoku', { difficulty: 'medium', seed: 7 }, playSudoku, ['inline', 'fullscreen']);
";

    // ── Breakout ────────────────────────────────────────────────────────

    private const string BreakoutCss = @"
:root{--bw:360px}
html[data-mode=fullscreen]{--bw:min(560px,calc((100vh - 230px) * .75))}
html[data-mode=fullscreen] body{padding:24px;background:#16181b}
html[data-mode=fullscreen] .card.gbreak{max-width:640px;min-height:0;margin:0 auto;padding:20px}
.gbreak{max-width:420px;margin:8px auto;padding:14px 16px 16px;font-family:'Courier New',monospace;color:#f2e9c9;background:linear-gradient(160deg,#4a4e54,#2c2f33 60%,#222427);border:0;border-radius:18px;box-shadow:inset 0 2px 0 rgba(255,255,255,.2),inset 0 -4px 10px rgba(0,0,0,.5),0 4px 12px rgba(0,0,0,.45)}
.gbreak .row.between{flex-wrap:wrap;gap:6px}
.gbreak h1{flex:1 1 100%;font-size:20px;line-height:24px;font-weight:700;letter-spacing:.3em;text-align:center;text-transform:uppercase;color:#f2e9c9;text-shadow:0 2px 0 #000}
.gbreak .stats{flex:1 1 100%;justify-content:space-between;gap:8px;padding:6px 10px;background:#000000;border-radius:6px;box-shadow:inset 0 0 0 2px #15171a}
.gbreak .stat{flex-direction:row;align-items:baseline;gap:6px;min-width:0}
.gbreak .stat .cap{font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#9aa0a6}
.gbreak .stat strong{font-size:20px;line-height:22px;color:#f2d12e}
.gbreak .boardwrap{padding:6px;background:#000000;border-radius:6px;box-shadow:inset 0 0 0 2px #15171a}
.gbreak .boardwrap:focus-visible{outline:2px solid #f2d12e;outline-offset:2px}
.gbreak .board{width:100%;max-width:var(--bw);background:#000000;border:0;border-radius:0;image-rendering:pixelated}
.gbreak .hold{display:flex;justify-content:center;gap:16px}
.gbreak .hold .btn{min-width:72px;height:40px;font-size:18px}
.gbreak .btn{color:#f2e9c9;background:linear-gradient(#5b5f66,#3a3d42);border:1px solid #15171a;border-radius:6px;box-shadow:0 2px 0 #15171a,inset 0 1px 0 rgba(255,255,255,.2);font-family:inherit;touch-action:none}
.gbreak .btn:hover:not(:disabled){background:linear-gradient(#6a6e75,#44474c);border-color:#15171a}
.gbreak .btn:active:not(:disabled){transform:translateY(2px);box-shadow:none}
.gbreak .btn.primary{color:#1a1500;background:linear-gradient(#f6dc5a,#c9a40f)}
.gbreak .btn.primary:hover:not(:disabled){background:linear-gradient(#ffe777,#d6b117)}
.gbreak .actions{justify-content:center}
";

    private const string BreakoutJs = @"
function playBreakout(data) {
  var W = 360, H = 480, COLS = 10, ROWS = 6, BW = 32, BH = 14, GAP = 2, TOP = 56;
  var LEFT = (W - (COLS * (BW + GAP) - GAP)) / 2, PW = 64, PH = 8, PY = H - 36, R = 5;
  var COLORS = ['#e8402f', '#f08a2a', '#f2d12e', '#3fbf55', '#2bb6d6', '#3a6fe0'], POINTS = [7, 7, 4, 4, 1, 1];
  var LAYOUTS = [
    function () { return true; },
    function (r, c) { return Math.abs(c - 4.5) <= 1 + r * 0.7; },
    function (r, c) { return (r + c) % 2 === 0; },
    function (r, c) { return c % 3 !== 2; },
    function (r, c) { return r === 0 || r === 5 || c === 0 || c === 9 || ((r === 2 || r === 3) && c >= 3 && c <= 6); }
  ];
  var base = { slow: 3.2, normal: 4.2, fast: 5.4 }[data.speed] || 4.2;
  var level = clamp(data.level, 1, 5, 1), lives = clamp(data.lives, 1, 5, 3), score = 0, startLevel = level;
  var canvas = h('canvas', { class: 'board', width: W, height: H, role: 'img', 'aria-label': 'Breakout board' });
  var ctx = canvas.getContext('2d');
  var keys = { left: false, right: false };
  var bricks = [], alive = 0, total = 0, paddle = W / 2, ball = { x: 0, y: 0, vx: 0, vy: 0 };
  var state = 'ready', raf = 0, last = 0, acc = 0;

  function hold(glyph, label, dir) {
    var b = h('button', { class: 'btn', type: 'button', 'aria-label': label, text: glyph });
    function on(e) { keys[dir] = true; try { b.setPointerCapture(e.pointerId); } catch (x) {} }
    function off() { keys[dir] = false; }
    b.addEventListener('pointerdown', on);
    b.addEventListener('pointerup', off);
    b.addEventListener('pointercancel', off);
    b.addEventListener('lostpointercapture', off);
    b.addEventListener('click', function (e) { if (e.detail === 0) nudge(dir === 'left' ? -24 : 24); });
    return b;
  }

  var kit = gameShell({
    title: 'Breakout', board: canvas, cls: 'gbreak',
    stats: [['score', 'Score'], ['lives', 'Lives'], ['level', 'Level']],
    controls: h('div', { class: 'hold' }, hold('\u25C0', 'Move left', 'left'), hold('\u25B6', 'Move right', 'right'))
  });

  function nudge(dx) { paddle = Math.max(PW / 2, Math.min(W - PW / 2, paddle + dx)); }

  function stats() { kit.stat('score', score); kit.stat('lives', lives); kit.stat('level', level); }

  function build() {
    bricks = []; alive = 0;
    var layout = LAYOUTS[(level - 1) % LAYOUTS.length];
    for (var r = 0; r < ROWS; r++) {
      for (var c = 0; c < COLS; c++) {
        if (!layout(r, c)) continue;
        bricks.push({ x: LEFT + c * (BW + GAP), y: TOP + r * (BH + GAP), row: r, on: true });
        alive++;
      }
    }
    total = alive;
  }

  function speed() { return base * (1 + 0.05 * (level - 1)) * (1 + 0.45 * (total - alive) / total); }

  function ready() {
    state = 'ready';
    ball.x = paddle; ball.y = PY - R - 1; ball.vx = 0; ball.vy = 0;
    kit.label('Launch');
  }

  function launch() {
    if (state !== 'ready') return;
    var a = (Math.random() * 50 - 25) * Math.PI / 180, s = speed();
    ball.vx = s * Math.sin(a); ball.vy = -s * Math.cos(a);
    state = 'running';
    kit.label('Pause');
  }

  function rescale() {
    var s = Math.sqrt(ball.vx * ball.vx + ball.vy * ball.vy), t = speed();
    if (!s) return;
    var minY = 0.28 * s;
    if (Math.abs(ball.vy) < minY) { ball.vy = (ball.vy < 0 ? -1 : 1) * minY; s = Math.sqrt(ball.vx * ball.vx + ball.vy * ball.vy); }
    ball.vx *= t / s; ball.vy *= t / s;
  }

  function lose() {
    lives--;
    stats();
    if (lives <= 0) { over(false); return; }
    kit.announce('Ball lost. ' + lives + (lives === 1 ? ' life left.' : ' lives left.'));
    ready();
  }

  function over(won) {
    state = won ? 'won' : 'over';
    kit.label('Play again');
    kit.announce(won ? 'You cleared every level with a score of ' + score + '.' : 'Game over. You scored ' + score + '.');
    kit.report('The user finished a game of Breakout' + (won ? ' and cleared every level' : ' reaching level ' + level) + ' with a score of ' + score + '.',
      { game: 'breakout', score: score, level: level, livesLeft: Math.max(0, lives), won: won });
  }

  function clearedLevel() {
    if (level >= 5) { over(true); return; }
    level++;
    build(); stats(); ready();
    kit.announce('Level cleared. Level ' + level + '.');
  }

  function move(f) {
    var px = ball.x, py = ball.y;
    ball.x += ball.vx * f; ball.y += ball.vy * f;
    if (ball.x < R) { ball.x = R; ball.vx = Math.abs(ball.vx); rescale(); }
    else if (ball.x > W - R) { ball.x = W - R; ball.vx = -Math.abs(ball.vx); rescale(); }
    if (ball.y < R) { ball.y = R; ball.vy = Math.abs(ball.vy); rescale(); }

    if (ball.vy > 0 && py + R <= PY + 3 && ball.y + R >= PY && ball.y - R <= PY + PH &&
        ball.x >= paddle - PW / 2 - R && ball.x <= paddle + PW / 2 + R) {
      var hit = Math.max(-1, Math.min(1, (ball.x - paddle) / (PW / 2))), a = hit * 62 * Math.PI / 180, s = speed();
      ball.y = PY - R;
      ball.vx = s * Math.sin(a); ball.vy = -s * Math.cos(a);
      return;
    }
    if (ball.y - R > H) { lose(); return; }

    for (var i = 0; i < bricks.length; i++) {
      var b = bricks[i];
      if (!b.on) continue;
      var cx = Math.max(b.x, Math.min(ball.x, b.x + BW)), cy = Math.max(b.y, Math.min(ball.y, b.y + BH));
      var dx = ball.x - cx, dy = ball.y - cy;
      if (dx * dx + dy * dy > R * R) continue;
      var inX = px >= b.x && px <= b.x + BW, inY = py >= b.y && py <= b.y + BH;
      if (inX && !inY) ball.vy = -ball.vy;
      else if (inY && !inX) ball.vx = -ball.vx;
      else { ball.vx = -ball.vx; ball.vy = -ball.vy; }
      b.on = false; alive--;
      score += POINTS[b.row];
      stats();
      rescale();
      if (alive === 0) clearedLevel();
      return;
    }
  }

  function step() {
    var dir = (keys.left ? -1 : 0) + (keys.right ? 1 : 0);
    if (dir) nudge(dir * 6);
    if (state === 'ready') { ball.x = paddle; ball.y = PY - R - 1; return; }
    if (state !== 'running') return;
    var s = Math.sqrt(ball.vx * ball.vx + ball.vy * ball.vy), n = Math.max(1, Math.ceil(s / 2));
    for (var k = 0; k < n && state === 'running'; k++) move(1 / n);
  }

  function draw() {
    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, W, H);
    bricks.forEach(function (b) {
      if (!b.on) return;
      ctx.fillStyle = COLORS[b.row];
      ctx.fillRect(b.x, b.y, BW, BH);
    });
    ctx.fillStyle = '#c9ccd1';
    ctx.fillRect(paddle - PW / 2, PY, PW, PH);
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(ball.x - R, ball.y - R, R * 2, R * 2);
    if (state === 'ready' || state === 'paused' || state === 'over' || state === 'won') {
      ctx.fillStyle = '#f2d12e';
      ctx.font = 'bold 20px ""Courier New"", monospace';
      ctx.textAlign = 'center';
      ctx.fillText(state === 'paused' ? 'PAUSED' : state === 'over' ? 'GAME OVER' : state === 'won' ? 'YOU WIN' : 'TAP TO LAUNCH', W / 2, H / 2 + 60);
      var sub = state === 'over' || state === 'won' ? 'SCORE ' + score : state === 'ready' && level > startLevel ? 'LEVEL ' + level : '';
      if (sub) ctx.fillText(sub, W / 2, H / 2 + 90);
    }
  }

  function frame(t) {
    raf = requestAnimationFrame(frame);
    if (!last) last = t;
    acc += Math.min(100, t - last);
    last = t;
    for (var n = 0; acc >= 16.667 && n < 5; n++) { step(); acc -= 16.667; }
    draw();
  }

  function pause() {
    if (state !== 'running') return;
    state = 'paused';
    keys.left = false; keys.right = false;
    kit.label('Resume');
  }

  function act() {
    if (state === 'ready') launch();
    else if (state === 'running') pause();
    else if (state === 'paused') { state = 'running'; kit.label('Pause'); }
    else restart();
  }

  function restart() {
    level = clamp(data.level, 1, 5, 1); lives = clamp(data.lives, 1, 5, 3); score = 0;
    build(); stats(); ready();
  }

  function aim(e) {
    var rect = canvas.getBoundingClientRect();
    if (!rect.width) return;
    paddle = Math.max(PW / 2, Math.min(W - PW / 2, (e.clientX - rect.left) * W / rect.width));
  }

  function up(e) { if (e.key === 'ArrowLeft' || e.key === 'a') keys.left = false; if (e.key === 'ArrowRight' || e.key === 'd') keys.right = false; }

  canvas.addEventListener('pointermove', aim);
  canvas.addEventListener('pointerdown', function (e) { aim(e); if (state === 'ready') launch(); });
  document.addEventListener('keyup', up);
  kit.onPrimary(act);
  kit.onAway(function () { pause(); keys.left = false; keys.right = false; });
  kit.onStop(function () { cancelAnimationFrame(raf); document.removeEventListener('keyup', up); });
  kit.onKey(function (key, onBoard) {
    if (key === 'ArrowLeft' || key === 'a') { keys.left = true; return true; }
    if (key === 'ArrowRight' || key === 'd') { keys.right = true; return true; }
    if (onBoard && (key === ' ' || key === 'Enter')) { act(); return true; }
    return false;
  });
  build(); stats(); ready();
  raf = requestAnimationFrame(frame);
}

start('Breakout', { level: 1, lives: 3, speed: 'normal' }, playBreakout, ['inline', 'fullscreen']);
";

    // ── Solitaire ───────────────────────────────────────────────────────

    private const string SolitaireCss = @"
.gsol{--cw:clamp(30px,calc((100vw - 76px) / 7 - 8px),46px);--gap:8px;width:max-content;max-width:100%;min-width:0;margin:8px auto;font-family:Tahoma,'Segoe UI',Arial,sans-serif}
html[data-mode=fullscreen] .gsol{--cw:clamp(30px,calc((100vw - 110px) / 7 - 8px),76px)}
html[data-mode=fullscreen] .card.gsol{width:max-content}
.gsol .screen{display:flex;flex-direction:column-reverse;gap:4px}
.gsol .stats{flex:1 1 100%;flex-wrap:nowrap;gap:0;margin:0;padding:0;box-shadow:none}
.gsol .stat{flex:1 1 0;flex-direction:row;align-items:baseline;gap:4px;min-width:0;padding:1px 6px;box-shadow:var(--sunken)}
.gsol .stat .cap{font-size:12px;color:#000000}
.gsol .stat .cap::after{content:':'}
.gsol .stat strong{font-size:12px;font-weight:400}
.gsol .stat+.stat{margin-left:2px}
.gsol .intro{min-height:16px;font-size:12px;color:#000000;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.gsol .boardwrap{display:block;touch-action:manipulation}
.gsol .boardwrap:focus-visible{outline:2px solid var(--focus);outline-offset:-2px}
.felt{position:relative;padding:8px;background:#008000;border:2px solid;border-color:#808080 #ffffff #ffffff #808080}
.sol-top,.sol-tab{display:grid;grid-template-columns:repeat(7,var(--cw));gap:var(--gap)}
.sol-top{margin-bottom:12px}
.sol-tab{align-items:start;min-height:calc(var(--cw) * 7.9)}
.slot{position:relative;width:var(--cw);height:calc(var(--cw) * 1.4);border:1px solid #004d00;border-radius:3px;background:rgba(0,0,0,.18);color:#c8f0c8;font-size:calc(var(--cw) * .5);text-align:center;line-height:calc(var(--cw) * 1.4)}
.slot.live{cursor:pointer}
.col{position:relative;width:var(--cw);min-height:calc(var(--cw) * 1.4)}
.col.empty{border:1px solid #004d00;border-radius:3px;background:rgba(0,0,0,.18)}
.crd{position:absolute;top:0;left:0;display:block;width:var(--cw);height:calc(var(--cw) * 1.4);padding:0;font:inherit;font-family:Arial,'Segoe UI',sans-serif;font-size:calc(var(--cw) * .24);font-weight:700;line-height:1;text-align:left;color:#000000;background:#ffffff;border:1px solid #000000;border-radius:3px;cursor:pointer;touch-action:manipulation}
.crd:disabled{cursor:default;opacity:1}
.crd.r{color:#cc0000}
.crd .tl{position:absolute;top:calc(var(--cw) * .04);left:calc(var(--cw) * .06);white-space:nowrap}
.crd .br{position:absolute;right:calc(var(--cw) * .06);bottom:calc(var(--cw) * .04);white-space:nowrap;transform:rotate(180deg)}
.crd .art{position:absolute;left:24%;right:24%;top:28%;bottom:28%}
.crd .pip{position:absolute;font-size:calc(var(--cw) * .2);line-height:1;transform:translate(-50%,-50%)}
.crd .pip.f{transform:translate(-50%,-50%) rotate(180deg)}
.crd .pip.big{font-size:calc(var(--cw) * .6)}
.crd .art.face{left:22%;right:22%;top:26%;bottom:26%;display:grid;place-items:center;background:linear-gradient(135deg,#ffe36a 50%,#7aa8ff 50%);border:1px solid #000000}
.crd .art.face .fl{display:flex;flex-direction:column;align-items:center;font-size:calc(var(--cw) * .36);line-height:1}
.crd .art.face .fl small{font-size:.55em}
.crd.dn{background:#2a2aa8 var(--back) center/100% 100% no-repeat;box-shadow:inset 0 0 0 3px #ffffff}
.bkthumb{display:inline-block;width:14px;height:20px;margin-right:6px;vertical-align:middle;background:#2a2aa8 var(--back) center/100% 100% no-repeat;border:1px solid #000000}
.crd.sel{box-shadow:0 0 0 3px #ffff00;z-index:2}
.crd:focus-visible{outline:2px solid #ffff00;outline-offset:1px;z-index:3}
.cascade{position:absolute;inset:0;width:100%;height:100%;pointer-events:none}
.gsol .ctl{display:flex;gap:8px;flex-wrap:wrap}
";

    private const string SolitaireJs = @"
function cardColor(s) { return s === 1 || s === 2 ? 'r' : 'b'; }

function canFound(card, pile) {
  if (!pile.length) return card.r === 1;
  var top = pile[pile.length - 1];
  return top.s === card.s && top.r === card.r - 1;
}

function canTab(card, col) {
  if (!col.length) return card.r === 13;
  var top = col[col.length - 1];
  return top.u && cardColor(top.s) !== cardColor(card.s) && top.r === card.r + 1;
}

function solDeal(num) {
  var deck = [], s, r, c, i, k = 0, tab = [];
  for (s = 0; s < 4; s++) for (r = 1; r <= 13; r++) deck.push({ s: s, r: r, u: false });
  shuffle(deck, mulberry32(num));
  for (c = 0; c < 7; c++) {
    var col = [];
    for (i = 0; i <= c; i++) { var card = deck[k++]; card.u = i === c; col.push(card); }
    tab.push(col);
  }
  return { stock: deck.slice(k), waste: [], found: [[], [], [], []], tab: tab, score: 0, moves: 0 };
}

// Original card back artwork, drawn as small SVG scenes on a 50 by 70 grid.
var SQ = String.fromCharCode(39), DQ = String.fromCharCode(34);

function svEl(tag, a, inner) {
  var s = '<' + tag;
  for (var k in a) s += ' ' + k + '=' + SQ + a[k] + SQ;
  return s + (inner === undefined ? '/>' : '>' + inner + '</' + tag + '>');
}
function svR(x, y, w, hh, f) { return svEl('rect', { x: x, y: y, width: w, height: hh, fill: f }); }
function svP(pts, f) { return svEl('polygon', { points: pts, fill: f }); }
function svE(cx, cy, rx, ry, f) { return svEl('ellipse', { cx: cx, cy: cy, rx: rx, ry: ry, fill: f }); }
function svL(x1, y1, x2, y2, c, w) { return svEl('line', { x1: x1, y1: y1, x2: x2, y2: y2, stroke: c, 'stroke-width': w, 'stroke-linecap': 'round' }); }
function svFish(x, y, f) {
  return svEl('g', { transform: 'translate(' + x + ' ' + y + ') scale(' + f + ' 1)' },
    svP('-8,0 -14,-5 -14,5', '#ff7a00') + svE(0, 0, 8, 4.6, '#ff7a00') + svE(-1, 0, 1.2, 3.6, '#ffffff') +
    svE(3, 0, 1.2, 3.4, '#ffffff') + svR(5, -1.6, 1.6, 1.6, '#000000'));
}

var BACK_NAMES = ['Weave', 'Robot', 'Fish', 'Reef', 'Shell', 'Castle', 'Beach', 'Cards in hand'];
var BACKS = [
  function () {
    return svEl('defs', {}, svEl('pattern', { id: 'w', width: 8, height: 8, patternUnits: 'userSpaceOnUse' },
      svR(0, 0, 8, 8, '#1f2fb0') + svR(0, 0, 4, 4, '#e8483c') + svR(4, 4, 4, 4, '#e8483c') + svR(1, 5, 2, 2, '#9ad0ff') + svR(5, 1, 2, 2, '#9ad0ff'))) +
      svEl('rect', { width: 50, height: 70, fill: 'url(#w)' });
  },
  function () {
    return svR(0, 0, 50, 70, '#1c1ce0') + svR(24, 5, 2, 7, '#e03020') + svR(22, 3, 6, 3, '#e03020') + svR(14, 12, 22, 15, '#e03020') +
      svR(16, 14, 18, 11, '#f4c542') + svR(18, 16, 5, 5, '#ffffff') + svR(27, 16, 5, 5, '#ffffff') + svR(20, 18, 2, 2, '#000000') +
      svR(29, 18, 2, 2, '#000000') + svR(20, 23, 10, 2, '#7a1a10') + svR(21, 27, 8, 3, '#a01810') + svR(10, 30, 30, 22, '#e03020') +
      svR(15, 33, 20, 11, '#f4c542') + svR(17, 35, 6, 3, '#e03020') + svR(26, 35, 3, 3, '#000000') + svR(31, 35, 3, 3, '#000000') +
      svR(17, 40, 16, 2, '#7a1a10') + svR(4, 31, 6, 16, '#e03020') + svR(40, 31, 6, 16, '#e03020') + svR(3, 47, 8, 5, '#f4c542') +
      svR(39, 47, 8, 5, '#f4c542') + svR(14, 52, 8, 11, '#e03020') + svR(28, 52, 8, 11, '#e03020') + svR(11, 63, 12, 4, '#a01810') +
      svR(27, 63, 12, 4, '#a01810');
  },
  function () {
    return svR(0, 0, 50, 70, '#15c5d6') + svFish(20, 14, 1) + svFish(32, 28, -1) + svFish(20, 43, 1) + svFish(31, 57, -1);
  },
  function () {
    return svR(0, 0, 50, 70, '#2230d8') + svFish(30, 13, 1) + svFish(21, 29, -1) + svFish(31, 44, 1) + svR(0, 65, 50, 5, '#e8d080') +
      svL(8, 66, 8, 54, '#2eb82e', 3) + svL(14, 66, 13, 57, '#2eb82e', 3) + svL(41, 66, 42, 52, '#2eb82e', 3);
  },
  function () {
    return svR(0, 0, 50, 70, '#ffe9b8') + svP('25,6 40,48 32,64 18,64 10,48', '#c98a5a') + svP('25,6 40,48 32,64 28,64', '#a86a3e') +
      svL(20, 21, 30, 23, '#fbe9cc', 2) + svL(16, 31, 35, 34, '#fbe9cc', 2) + svL(13, 41, 38, 44, '#fbe9cc', 2) + svL(12, 51, 36, 53, '#fbe9cc', 2) +
      svE(25, 63, 7, 2, '#8a5530');
  },
  function () {
    return svR(0, 0, 50, 70, '#0b0b45') + svR(6, 6, 1, 1, '#ffffff') + svR(18, 10, 1, 1, '#ffffff') + svR(30, 5, 1, 1, '#ffffff') +
      svR(44, 22, 1, 1, '#ffffff') + svR(8, 22, 1, 1, '#ffffff') + svE(38, 12, 5, 5, '#fff2b0') +
      svP('0,70 0,50 12,42 36,46 50,54 50,70', '#000000') + svR(14, 26, 22, 18, '#000000') + svR(10, 18, 7, 26, '#000000') +
      svR(33, 18, 7, 26, '#000000') + svP('10,18 13.5,10 17,18', '#000000') + svP('33,18 36.5,10 40,18', '#000000') +
      svR(23, 32, 3, 4, '#ffd84a') + svR(12, 24, 2, 3, '#ffd84a') + svR(36, 24, 2, 3, '#ffd84a') + svP('22,70 30,70 27,50 25,50', '#7a7a95');
  },
  function () {
    return svR(0, 0, 50, 70, '#5bc8f5') + svE(40, 10, 6, 6, '#ffe033') + svR(0, 40, 50, 12, '#1878d8') + svR(0, 44, 50, 1, '#bfe6ff') +
      svR(0, 52, 50, 18, '#f3d878') + svP('22,62 27,62 27,36 24,34', '#8a5a2a') + svL(25, 33, 10, 28, '#1f9a3a', 4) +
      svL(25, 33, 40, 28, '#1f9a3a', 4) + svL(25, 33, 13, 40, '#1f9a3a', 4) + svL(25, 33, 37, 40, '#1f9a3a', 4) +
      svL(25, 33, 25, 21, '#1f9a3a', 4) + svE(24, 36, 2, 2, '#5a3a18');
  },
  function () {
    var card = function (turn, dx) {
      return svEl('g', { transform: 'rotate(' + turn + ' 24 36)' },
        svEl('rect', { x: 14 + dx, y: 6, width: 20, height: 28, fill: '#ffffff', stroke: '#000000', 'stroke-width': 1 }) +
        svP((24 + dx) + ',14 ' + (28 + dx) + ',20 ' + (24 + dx) + ',26 ' + (20 + dx) + ',20', '#d01010'));
    };
    return svR(0, 0, 50, 70, '#ffee50') + card(-18, 0) + card(8, 6) + svP('14,38 30,32 40,40 36,52 20,56', '#f2b895') +
      svP('0,56 18,48 44,70 0,70', '#c85a50') + svL(3, 63, 20, 53, '#ffffff', 1.5) + svL(3, 68, 27, 57, '#ffffff', 1.5) + svL(14, 68, 34, 62, '#ffffff', 1.5);
  }
];

function backUri(n) {
  var svg = svEl('svg', { xmlns: 'http://www.w3.org/2000/svg', viewBox: '0 0 50 70', preserveAspectRatio: 'none', 'shape-rendering': 'crispEdges' }, BACKS[n]());
  return 'url(' + DQ + 'data:image/svg+xml,' + encodeURIComponent(svg) + DQ + ')';
}

// Pip positions as [column 0 to 2, row 0 to 1]. The lower half is drawn upside down.
var PIPS = {
  1: [[1, .5]], 2: [[1, 0], [1, 1]], 3: [[1, 0], [1, .5], [1, 1]],
  4: [[0, 0], [2, 0], [0, 1], [2, 1]], 5: [[0, 0], [2, 0], [1, .5], [0, 1], [2, 1]],
  6: [[0, 0], [2, 0], [0, .5], [2, .5], [0, 1], [2, 1]],
  7: [[0, 0], [2, 0], [1, .25], [0, .5], [2, .5], [0, 1], [2, 1]],
  8: [[0, 0], [2, 0], [1, .25], [0, .5], [2, .5], [1, .75], [0, 1], [2, 1]],
  9: [[0, 0], [2, 0], [0, .33], [2, .33], [1, .5], [0, .67], [2, .67], [0, 1], [2, 1]],
  10: [[0, 0], [2, 0], [1, .17], [0, .33], [2, .33], [0, .67], [2, .67], [1, .83], [0, 1], [2, 1]]
};

function playSolitaire(data) {
  var DRAW = data.drawCount === 3 ? 3 : 1;
  var dealNo = data.deal !== undefined && data.deal !== null ? clamp(data.deal, 1, 2147483647, 1) : 1 + rand(999999);
  var SUITS = ['\u2660', '\u2665', '\u2666', '\u2663'], SUITN = ['spades', 'hearts', 'diamonds', 'clubs'];
  var RANKS = ['', 'A', '2', '3', '4', '5', '6', '7', '8', '9', '10', 'J', 'Q', 'K'];
  var RANKN = ['', 'Ace', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven', 'Eight', 'Nine', 'Ten', 'Jack', 'Queen', 'King'];
  var S, undo, sel = null, won = false, auto = null, started = false, tap = { key: '', t: 0 }, anim = 0, baseIntro = '';
  var felt = h('div', { class: 'felt' });
  var clock = makeTimer(function (s) { kit.stat('time', fmtTime(s)); });
  var undoBtn = h('button', { class: 'btn', type: 'button', text: 'Undo', onclick: undoMove });
  var back = clamp(data.cardBack, 1, 8, 1) - 1;
  var deckBtn = h('button', { class: 'btn', type: 'button', onclick: nextBack }, h('span', { class: 'bkthumb', 'aria-hidden': 'true' }), 'Deck');
  var kit = gameShell({
    title: 'Solitaire', board: felt, cls: 'gsol w95', intro: 'Klondike', screen: true,
    stats: [['score', 'Score'], ['time', 'Time'], ['moves', 'Moves']],
    controls: h('div', { class: 'ctl' }, undoBtn, deckBtn)
  });
  var introEl = document.querySelector('.gsol .intro');

  function setBack() {
    document.querySelector('.gsol').style.setProperty('--back', backUri(back));
    deckBtn.setAttribute('aria-label', 'Change the card back. Now: ' + BACK_NAMES[back]);
  }

  function nextBack() {
    back = (back + 1) % BACKS.length;
    setBack();
    kit.announce('Card back ' + (back + 1) + ' of ' + BACKS.length + ': ' + BACK_NAMES[back] + '.');
  }
  setBack();

  function name(c) { return RANKN[c.r] + ' of ' + SUITN[c.s]; }

  function stats() {
    kit.stat('score', S.score);
    kit.stat('moves', S.moves);
    undoBtn.disabled = !undo.length || won;
  }

  function startClock() { if (!started && !won) { started = true; clock.start(); } }

  function save() { undo.push(JSON.stringify(S)); if (undo.length > 200) undo.shift(); }

  function sameLoc(a, b) {
    return a.k === b.k && a.c === b.c && a.i === b.i;
  }

  function run(src) {
    if (src.k === 'waste') return S.waste.length ? [S.waste[S.waste.length - 1]] : [];
    if (src.k === 'found') { var p = S.found[src.i]; return p.length ? [p[p.length - 1]] : []; }
    return S.tab[src.c].slice(src.i);
  }

  function take(src) {
    if (src.k === 'waste') return [S.waste.pop()];
    if (src.k === 'found') return [S.found[src.i].pop()];
    return S.tab[src.c].splice(src.i);
  }

  function tryMove(src, dest) {
    var cards = run(src);
    if (!cards.length) return false;
    if (dest.k === 'tab') {
      if (src.k === 'tab' && src.c === dest.c) return false;
      if (!canTab(cards[0], S.tab[dest.c])) return false;
    } else {
      if (cards.length !== 1 || src.k === 'found' || !canFound(cards[0], S.found[dest.i])) return false;
    }
    save();
    startClock();
    var moved = take(src);
    if (dest.k === 'tab') {
      moved.forEach(function (c) { S.tab[dest.c].push(c); });
      if (src.k === 'waste') S.score += 5;
      else if (src.k === 'found') S.score = Math.max(0, S.score - 15);
    } else {
      S.found[dest.i].push(moved[0]);
      S.score += 10;
    }
    if (src.k === 'tab') {
      var col = S.tab[src.c];
      if (col.length && !col[col.length - 1].u) { col[col.length - 1].u = true; S.score += 5; }
    }
    S.moves++;
    sel = null;
    after();
    return true;
  }

  function draw() {
    if (auto || won) return;
    if (!S.stock.length && !S.waste.length) return;
    save();
    startClock();
    if (S.stock.length) {
      for (var n = 0; n < DRAW && S.stock.length; n++) { var c = S.stock.pop(); c.u = true; S.waste.push(c); }
    } else {
      while (S.waste.length) { var w = S.waste.pop(); w.u = false; S.stock.push(w); }
      if (DRAW === 1) S.score = Math.max(0, S.score - 100);
    }
    S.moves++;
    sel = null;
    after();
  }

  function undoMove() {
    if (auto || won || !undo.length) return;
    S = JSON.parse(undo.pop());
    sel = null;
    render();
  }

  function tryAutoFound(src) {
    var cards = run(src);
    if (cards.length !== 1) return false;
    if (src.k === 'tab' && src.i !== S.tab[src.c].length - 1) return false;
    for (var i = 0; i < 4; i++) if (canFound(cards[0], S.found[i])) return tryMove(src, { k: 'found', i: i });
    return false;
  }

  function onCard(w) {
    if (auto || won) return;
    var now = Date.now(), key = w.k + ':' + (w.c === undefined ? '' : w.c) + ':' + (w.i === undefined ? '' : w.i);
    var dbl = tap.key === key && now - tap.t < 400;
    tap = { key: key, t: now };
    if (dbl && w.k !== 'found') { sel = null; if (!tryAutoFound(w)) render(); return; }
    if (sel) {
      if (sameLoc(sel, w)) { sel = null; render(); return; }
      if (w.k !== 'waste' && tryMove(sel, w.k === 'tab' ? { k: 'tab', c: w.c } : { k: 'found', i: w.i })) return;
    }
    sel = w;
    render();
  }

  function onSlot(dest) {
    if (auto || won || !sel) return;
    if (!tryMove(sel, dest)) { sel = null; render(); }
  }

  function done() {
    for (var i = 0; i < 4; i++) if (S.found[i].length < 13) return false;
    return true;
  }

  function canAuto() {
    if (S.stock.length) return false;
    for (var c = 0; c < 7; c++) for (var i = 0; i < S.tab[c].length; i++) if (!S.tab[c][i].u) return false;
    return true;
  }

  function autoStep() {
    var srcs = [{ k: 'waste' }], c, i;
    for (c = 0; c < 7; c++) srcs.push({ k: 'tab', c: c, i: S.tab[c].length - 1 });
    for (i = 0; i < srcs.length; i++) {
      var s = srcs[i], cards = s.k === 'tab' && s.i < 0 ? [] : run(s);
      if (cards.length === 1) for (var p = 0; p < 4; p++) if (canFound(cards[0], S.found[p])) { return tryMove(s, { k: 'found', i: p }); }
    }
    return false;
  }

  function after() {
    render();
    if (done()) { win(); return; }
    if (!auto && canAuto()) {
      introEl.textContent = baseIntro + ' \u00B7 auto-complete';
      auto = setInterval(function () {
        if (!autoStep()) { clearInterval(auto); auto = null; introEl.textContent = baseIntro; }
      }, 90);
    }
  }

  function win() {
    clearInterval(auto); auto = null;
    won = true;
    clock.stop();
    var secs = clock.seconds();
    introEl.textContent = 'You won! Score ' + S.score + ' \u00B7 ' + fmtTime(secs) + ' \u00B7 ' + S.moves + ' moves';
    kit.announce('You won with a score of ' + S.score + '.');
    kit.report('The user won a game of Solitaire (draw ' + DRAW + ') with a score of ' + S.score + ' in ' + fmtTime(secs) + ' and ' + S.moves + ' moves.',
      { game: 'solitaire', won: true, score: S.score, moves: S.moves, seconds: secs, drawCount: DRAW, deal: dealNo });
    stats();
    cascade();
  }

  function cascade() {
    if (window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    var box = felt.getBoundingClientRect();
    if (!box.width) return;
    var cv = h('canvas', { class: 'cascade', width: Math.round(box.width), height: Math.round(box.height), 'aria-hidden': 'true' });
    felt.appendChild(cv);
    var g = cv.getContext('2d'), live = [], queue = [], t = 0, cw = Math.max(28, box.width / 8.6);
    for (var r = 13; r >= 1; r--) for (var s = 0; s < 4; s++) queue.push({ r: r, s: s });
    function step() {
      anim = requestAnimationFrame(step);
      t++;
      if (queue.length && t % 8 === 0) {
        var q = queue.shift();
        live.push({ r: q.r, s: q.s, x: cw * (3.2 + q.s * 1.15) + 8, y: 10, vx: (Math.random() < .5 ? -1 : 1) * (1.5 + Math.random() * 3.5), vy: -1 });
      }
      live.forEach(function (c) {
        c.vy += 0.4; c.x += c.vx; c.y += c.vy;
        var floor = cv.height - cw * 1.4;
        if (c.y > floor) { c.y = floor; c.vy = -c.vy * 0.82; }
        g.fillStyle = '#ffffff'; g.fillRect(c.x, c.y, cw, cw * 1.4);
        g.strokeStyle = '#000000'; g.strokeRect(c.x + .5, c.y + .5, cw - 1, cw * 1.4 - 1);
        g.fillStyle = c.s === 1 || c.s === 2 ? '#cc0000' : '#000000';
        g.font = 'bold ' + Math.round(cw * .3) + 'px Arial, sans-serif';
        g.fillText(RANKS[c.r] + SUITS[c.s], c.x + 3, c.y + cw * .32);
        g.font = Math.round(cw * .55) + 'px Arial, sans-serif';
        g.fillText(SUITS[c.s], c.x + cw * .3, c.y + cw * 1.15);
      });
      live = live.filter(function (c) { return c.x > -cw && c.x < cv.width + cw; });
      if (!queue.length && !live.length) cancelAnimationFrame(anim);
    }
    anim = requestAnimationFrame(step);
  }

  function faceArt(card) {
    if (card.r > 10) {
      return h('span', { class: 'art face', 'aria-hidden': 'true' }, h('span', { class: 'fl' }, RANKS[card.r], h('small', { text: SUITS[card.s] })));
    }
    var art = h('span', { class: 'art', 'aria-hidden': 'true' });
    PIPS[card.r].forEach(function (p) {
      art.appendChild(h('span', {
        class: 'pip' + (card.r === 1 ? ' big' : '') + (p[1] > .5 ? ' f' : ''),
        style: 'left:' + (p[0] * 50) + '%;top:' + (p[1] * 100) + '%', text: SUITS[card.s]
      }));
    });
    return art;
  }

  function cardEl(card, where, style, interactive) {
    var isSel = sel && sameLoc(sel, where) || (sel && sel.k === 'tab' && where.k === 'tab' && sel.c === where.c && where.i >= sel.i);
    var b = h('button', {
      class: 'crd' + (card.u ? (cardColor(card.s) === 'r' ? ' r' : '') : ' dn') + (isSel ? ' sel' : ''), type: 'button', style: style,
      'aria-label': card.u ? name(card) : 'Face-down card', disabled: !interactive || !card.u
    });
    if (card.u) {
      b.appendChild(h('span', { class: 'tl', 'aria-hidden': 'true', text: RANKS[card.r] + SUITS[card.s] }));
      b.appendChild(h('span', { class: 'br', 'aria-hidden': 'true', text: RANKS[card.r] + SUITS[card.s] }));
      b.appendChild(faceArt(card));
    }
    b.addEventListener('click', function (e) { e.stopPropagation(); onCard(where); });
    return b;
  }

  function render() {
    var cv = felt.querySelector('.cascade');
    clear(felt);
    if (cv && won) felt.appendChild(cv);
    var top = h('div', { class: 'sol-top' });
    var stock = h('div', { class: 'slot' + (S.stock.length || S.waste.length ? ' live' : ''), role: 'button', tabindex: '0', 'aria-label': S.stock.length ? 'Stock, ' + S.stock.length + ' cards. Draw.' : S.waste.length ? 'Stock empty. Turn the waste pile over.' : 'Stock empty' });
    stock.addEventListener('click', draw);
    stock.addEventListener('keydown', function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); e.stopPropagation(); draw(); } });
    if (S.stock.length) stock.appendChild(h('span', { class: 'crd dn', style: 'pointer-events:none' }));
    else if (S.waste.length) stock.textContent = '\u21BB';
    var waste = h('div', { class: 'slot', style: 'overflow:visible' });
    var shown = S.waste.slice(-DRAW);
    shown.forEach(function (c, i) {
      var isTop = i === shown.length - 1;
      waste.appendChild(cardEl(c, { k: 'waste' }, 'left:calc(var(--cw) * ' + (i * 0.3) + ')', isTop));
    });
    top.appendChild(stock); top.appendChild(waste); top.appendChild(h('div'));
    S.found.forEach(function (pile, i) {
      var slot = h('div', { class: 'slot' + (sel ? ' live' : ''), 'aria-label': 'Foundation ' + (i + 1) + (pile.length ? ', ' + name(pile[pile.length - 1]) : ', empty') });
      slot.addEventListener('click', function () { onSlot({ k: 'found', i: i }); });
      if (pile.length) slot.appendChild(cardEl(pile[pile.length - 1], { k: 'found', i: i }, '', true));
      top.appendChild(slot);
    });
    var tab = h('div', { class: 'sol-tab' });
    S.tab.forEach(function (col, c) {
      var down = col.filter(function (x) { return !x.u; }).length, up = col.length - down;
      var total = down * 0.2 + up * 0.36, k = total > 6.4 ? 6.4 / total : 1, y = 0;
      var el = h('div', { class: 'col' + (col.length ? '' : ' empty'), style: 'height:calc(var(--cw) * ' + (1.4 + total * k).toFixed(2) + ')' });
      el.addEventListener('click', function () { onSlot({ k: 'tab', c: c }); });
      col.forEach(function (card, i) {
        el.appendChild(cardEl(card, { k: 'tab', c: c, i: i }, 'top:calc(var(--cw) * ' + y.toFixed(2) + ')', true));
        y += (card.u ? 0.36 : 0.2) * k;
      });
      tab.appendChild(el);
    });
    felt.appendChild(top); felt.appendChild(tab);
    stats();
    App.resize();
  }

  function begin(fresh) {
    if (fresh && S && S.moves > 0 && !won) {
      kit.report('The user left a game of Solitaire (draw ' + DRAW + ') unfinished with a score of ' + S.score + ' after ' + S.moves + ' moves.',
        { game: 'solitaire', won: false, score: S.score, moves: S.moves, seconds: clock.seconds(), drawCount: DRAW, deal: dealNo });
    }
    clearInterval(auto); auto = null; cancelAnimationFrame(anim);
    if (fresh) dealNo = 1 + rand(999999);
    S = solDeal(dealNo);
    undo = []; sel = null; won = false; started = false;
    clock.reset();
    baseIntro = 'Klondike \u00B7 draw ' + DRAW + ' \u00B7 deal ' + dealNo;
    introEl.textContent = baseIntro;
    kit.label('New game');
    render();
  }

  kit.onPrimary(function () { begin(true); });
  kit.onAway(function () { if (started && !won) { clock.stop(); started = false; } });
  kit.onStop(function () { clock.stop(); clearInterval(auto); cancelAnimationFrame(anim); });
  kit.onKey(function (key, onBoard, target) {
    var k = key.length === 1 ? key.toLowerCase() : key;
    if (k === 'u') { undoMove(); return true; }
    if (onBoard && (k === ' ' || k === 'Enter')) { draw(); return true; }
    return false;
  });
  begin(false);
}

start('Solitaire', { drawCount: 1, deal: 1, cardBack: 1 }, playSolitaire, ['inline', 'fullscreen']);
";
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  SECTION 4: CYBER TRIVIA QUESTION BANK                                     ║
// ║                                                                            ║
// ║  Vetted security-awareness questions for company end users. The bank is    ║
// ║  fixed on purpose: the agent personalizes the quiz but does not write the  ║
// ║  guidance. Each question is scenario-based, has one clearly best answer,   ║
// ║  and carries a source note. A security-awareness owner should review the   ║
// ║  bank before it is published, and bump Version when it changes.            ║
// ║                                                                            ║
// ║  Guidance basis: CISA (Secure Our World, Recognize and Report Phishing),   ║
// ║  NIST SP 800-63B (length over complexity, no forced rotation), and         ║
// ║  Microsoft security documentation. {reportTo} and {company} are filled in  ║
// ║  from the tool arguments.                                                  ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

internal sealed class TriviaQuestion
{
    public string Category;
    public string Level;
    public string Stem;
    public string Correct;
    public string[] Wrong;
    public string Explain;
    public string Action;
}

internal static class TriviaBank
{
    public const string Version = "2026.1";

    public static readonly string[] Categories =
    {
        "phishing", "passwords", "social-engineering", "safe-browsing", "data-handling",
        "devices", "remote-work", "ai-use", "physical", "reporting"
    };

    public static string CategoryName(string key)
    {
        switch (key)
        {
            case "phishing": return "Phishing";
            case "passwords": return "Passwords and MFA";
            case "social-engineering": return "Social engineering";
            case "safe-browsing": return "Safe browsing";
            case "data-handling": return "Data handling";
            case "devices": return "Devices";
            case "remote-work": return "Remote work";
            case "ai-use": return "Using AI safely";
            case "physical": return "Physical security";
            case "reporting": return "Reporting incidents";
            case "company": return "Company policy";
            default: return key;
        }
    }

    private static TriviaQuestion Q(string cat, string level, string stem, string right, string w1, string w2, string w3, string why, string act)
    {
        return new TriviaQuestion { Category = cat, Level = level, Stem = stem, Correct = right, Wrong = new[] { w1, w2, w3 }, Explain = why, Action = act };
    }

    public static readonly List<TriviaQuestion> All = new List<TriviaQuestion>
    {
        // ── Phishing (CISA: Recognize and Report Phishing) ──
        Q("phishing", "basic", "An email from 'IT Support' says your mailbox is full and asks you to click a link to keep your account. What should you do?",
            "Do not click the link; report the email to {reportTo}", "Click the link and sign in quickly before the deadline", "Reply and ask the sender whether the email is real", "Forward it to coworkers so they can warn each other",
            "Urgent warnings that push you to click are a classic phishing tactic. Replying or forwarding can spread the risk.",
            "Use the report button or send it to {reportTo}, then delete it."),
        Q("phishing", "basic", "Before clicking a link in an email, what is the safest way to see where it really goes?",
            "Hover over it, or press and hold on a phone, to preview the address", "Trust the words shown on the link", "Check that the email has the company logo", "Click once, then close the page quickly",
            "Link text can say anything; the preview shows the real destination. Logos are easy to copy.",
            "If the address looks wrong or you are unsure, do not click. Report it."),
        Q("phishing", "basic", "A text claiming to be your bank has a link to 'verify a payment'. What is the best response?",
            "Ignore the link and contact the bank using the number on your card or its official app", "Tap the link, since banks send texts", "Reply STOP to unsubscribe", "Call the phone number shown in the text",
            "Text messages with links (smishing) copy trusted brands. Contact details you already know keep you away from the attacker.",
            "Delete the text and report it as spam in your messaging app."),
        Q("phishing", "basic", "You get an unexpected attachment named 'Invoice_2291.html' from someone you do not know. What should you do?",
            "Do not open it; report the email to {reportTo}", "Open it in a private browser window to be safe", "Open it as long as your antivirus is on", "Save it and open it later at home",
            "Unknown attachments can run harmful code or open fake sign-in pages. Private windows and antivirus do not make opening them safe.",
            "Report the email and delete it."),
        Q("phishing", "intermediate", "A message that seems to be from your CEO asks you to quickly buy gift cards and keep it secret. What is best?",
            "Check with the CEO through a separate, known channel before doing anything", "Buy the cards, since the CEO asked", "Reply to the email to ask for details", "Buy one card first to show good faith",
            "Pretending to be an executive is a common business email compromise tactic. Urgency plus secrecy is a warning sign.",
            "Report the message to {reportTo}."),
        Q("phishing", "intermediate", "You find a QR code on a lobby flyer offering free lunch if you sign in with your work account. What should you do?",
            "Do not scan it; report the flyer to {reportTo}", "Scan it, because a QR code is only an image", "Scan it, but use a different password", "Scan it with your personal phone instead",
            "A QR code hides its destination, and attackers use them to steal sign-ins (quishing). A different phone does not make a fake page safe.",
            "Take a photo of the flyer for {reportTo}, and leave it where it is."),
        Q("phishing", "intermediate", "Which web address is most likely a fake sign-in page for example.com?",
            "example.com.account-verify.net", "login.example.com", "www.example.com/signin", "accounts.example.com",
            "The real site is the name just before the first single slash. Here that is account-verify.net, with example.com only as a decoy.",
            "Read addresses from right to left up to the first slash, and sign in only on sites you already know."),
        Q("phishing", "intermediate", "A coworker's account sends you a file-share link with only 'See this?' as the message. What is the safest move?",
            "Check with them by chat or phone before opening it", "Open it, since it came from a coworker", "Open it, but do not type any passwords", "Share it with the team to see if anyone recognizes it",
            "Attackers use stolen accounts to phish people who trust the sender. A short, vague message is a hint.",
            "If they did not send it, tell {reportTo} so their account can be secured."),

        // ── Passwords and MFA (NIST SP 800-63B, CISA Secure Our World) ──
        Q("passwords", "basic", "Which is the best way to create a strong password for a work account?",
            "A long passphrase made of several unrelated words", "A short password with many symbols", "Your name and birth year", "A word with some letters swapped for numbers",
            "Length matters more than complexity. Predictable swaps and personal details are easy to guess.",
            "Use a passphrase of four or more random words, unique to each account."),
        Q("passwords", "basic", "What is the best way to keep track of many different passwords?",
            "Use a password manager approved by {company}", "Reuse one strong password everywhere", "Keep them in a spreadsheet on your desktop", "Write them on a sticky note under your keyboard",
            "A password manager lets you use a unique password for every account without remembering them all.",
            "Ask {reportTo} which password manager is approved."),
        Q("passwords", "basic", "A coworker asks for your password so they can finish a task while you are away. What should you do?",
            "Decline, and ask their manager or IT to give them the right access", "Share it, then change it later", "Share it, but only over chat", "Share it if they promise not to tell anyone",
            "Anything done with your password is traced to you. People who need access should get their own.",
            "Point them to IT or their manager."),
        Q("passwords", "basic", "Why should you turn on multifactor authentication (MFA) for your accounts?",
            "It can still protect your account if your password is stolen", "It lets you use a weaker password", "It makes signing in faster", "It means you no longer need to lock your computer",
            "A second proof of identity stops most attacks that start with a stolen password.",
            "Turn on MFA everywhere it is offered."),
        Q("passwords", "intermediate", "You receive several MFA approval prompts you did not start. What should you do?",
            "Deny them and report it to {reportTo} right away", "Approve one so the prompts stop", "Ignore them, because they will expire", "Approve the next one if they keep coming",
            "Repeated prompts mean someone has your password and hopes you will approve (MFA fatigue).",
            "Change your password and tell {reportTo}."),
        Q("passwords", "intermediate", "Which sign-in method is generally the most resistant to phishing?",
            "A passkey or hardware security key", "A code sent by text message", "Answers to security questions", "An email code you type into any page that asks",
            "Passkeys and security keys work only with the real site, so a fake page cannot reuse them. Codes can be tricked out of you.",
            "Use a passkey or security key where your company supports one."),
        Q("passwords", "intermediate", "A site you use had a data breach, and you used that same password on other sites. What should you do?",
            "Change it everywhere you used it, with a different password each time", "Change it only on the breached site", "Add a number to the end and keep using it", "Wait to see if anything suspicious happens",
            "Attackers try breached passwords on other sites (credential stuffing). Small changes are easy to guess.",
            "Check your accounts for odd activity and turn on MFA."),
        Q("passwords", "intermediate", "Current NIST guidance advises against forced regular password changes. When should you change a password?",
            "When you suspect it was exposed or stolen", "Every 30 days, no matter what", "Only when you forget it", "Each time you travel",
            "Frequent forced changes lead to weaker, predictable passwords. A change matters when there is a reason.",
            "Follow {company}'s password policy, which may add its own rules."),

        // ── Social engineering (CISA, Microsoft security documentation) ──
        Q("social-engineering", "basic", "A caller says they are from IT and need your password to fix a problem right now. What should you do?",
            "Hang up and contact IT using a number you already trust", "Give it, if they know your name", "Give them half of it", "Ask them to email you so you can reply with it",
            "Real IT teams do not ask for your password. Pressure to act now is how social engineers rush people.",
            "Report the call to {reportTo}."),
        Q("social-engineering", "basic", "Which is a common warning sign of a social engineering attempt?",
            "Pressure to act urgently and skip the normal steps", "A polite greeting", "A request that follows company procedure", "A message written in your language",
            "Attackers create urgency so you do not stop to think or verify.",
            "Slow down and verify through a channel you already trust."),
        Q("social-engineering", "basic", "Someone calls and asks you to read out the code that was just sent to your phone. What is the right response?",
            "Never share it; hang up and report the call", "Read it, if they know your name", "Read it, if they say they are from IT", "Read only the last three digits",
            "That code is the key to your account. No real support person needs it.",
            "Report the call to {reportTo}."),
        Q("social-engineering", "basic", "A stranger online asks detailed questions about your company's projects and staff. What should you do?",
            "Share only public information, and tell {reportTo} if it continues", "Answer, if they seem friendly", "Answer, if it might help a project", "Answer only about people who left",
            "Small details help attackers build trust and plan later attacks.",
            "Do not confirm names, systems, or plans."),
        Q("social-engineering", "intermediate", "A 'vendor' asks for a coworker's mobile number and manager's name for an urgent delivery. What is best?",
            "Decline, and ask them to use the official vendor contact process", "Share the number, since it is only contact information", "Share the manager's name but not the number", "Give your own number so they can call you",
            "Pretexting builds a story with small facts that look harmless. Contact details can be used to impersonate people.",
            "Tell {reportTo} if the vendor pushes back."),
        Q("social-engineering", "intermediate", "A voicemail sounds exactly like your manager asking for an urgent wire transfer. What should you remember?",
            "AI can copy voices, so confirm through a separate, known channel", "If it sounds like them, it is them", "Do the transfer, then confirm afterward", "Reply by text to the number that called",
            "Voice cloning is cheap and convincing. Urgent money requests always need a second check.",
            "Call your manager on a number you already have, and tell {reportTo}."),
        Q("social-engineering", "intermediate", "A job applicant's résumé file asks you to enable macros to view it. What should you do?",
            "Do not enable macros; report it to {reportTo}", "Enable macros, because résumés need them", "Enable macros only on a personal laptop", "Enable macros, then scan the file",
            "Macros in documents from outside can run code and install malware. A real résumé does not need them.",
            "Ask the sender for a PDF, or report the file."),
        Q("social-engineering", "intermediate", "A caller offers a gift card for a quick survey about your company's software and laptops. What is the risk?",
            "Your answers can reveal details attackers use to target you", "There is none, because surveys are harmless", "Only the gift card could be fake", "Only your name could be misused",
            "Information gathering is often the first step of an attack. Technology details help attackers pick their approach.",
            "Politely decline and tell {reportTo}."),

        // ── Safe browsing (CISA, Microsoft security documentation) ──
        Q("safe-browsing", "basic", "What does the padlock icon in a browser's address bar tell you?",
            "The connection is encrypted, but the site may still be harmful", "The site is run by a trustworthy company", "The site is free of malware", "The site is approved by your company",
            "Many fake sites also use encryption. The padlock protects the connection, not the site's intentions.",
            "Check the address itself, not just the padlock."),
        Q("safe-browsing", "basic", "A pop-up says your computer is infected and tells you to call a number or download a cleaner. What should you do?",
            "Close the tab or window, and report it if it keeps appearing", "Call the number right away", "Download the cleaner", "Click Allow so it can scan your computer",
            "Fake virus alerts are tech-support scams. Real security tools do not ask you to phone a stranger.",
            "If you cannot close it, tell {reportTo}."),
        Q("safe-browsing", "basic", "Where should you download work software from?",
            "The software portal approved by {company}", "The first search result", "A link in an email", "A site offering it free with a crack",
            "Unofficial downloads are a common way to spread malware.",
            "Ask {reportTo} if something you need is not in the portal."),
        Q("safe-browsing", "basic", "A website asks to send you notifications, and you are not sure why. What is the safest choice?",
            "Block it, unless you trust the site and want the alerts", "Allow it to make the prompt go away", "Allow it for 24 hours", "Allow it, then clear your cookies",
            "Notification permissions are used for spam and scams. Blocking costs you nothing.",
            "You can change permissions in your browser's site settings."),
        Q("safe-browsing", "intermediate", "You are at a café and need to sign in to a work system. What is the safest option?",
            "Use the company VPN, or your phone's hotspot", "Use the café's open Wi-Fi, because the site shows a padlock", "Use a free VPN app you found", "Join any network named after the café",
            "Open networks can be copied by attackers (evil twins). An approved VPN or your own hotspot protects your connection.",
            "Avoid sensitive work on public Wi-Fi when you can."),
        Q("safe-browsing", "intermediate", "Your browser warns that a site's security certificate is invalid. What should you do?",
            "Go back and do not continue to the site", "Click Advanced and continue, since warnings are common", "Continue, but type nothing", "Continue in a private window",
            "An invalid certificate can mean someone is intercepting your connection or the site is fake.",
            "If it is a work site, tell {reportTo}."),
        Q("safe-browsing", "intermediate", "What is the risk of installing a browser extension from an unknown publisher?",
            "It can read what you see and type on web pages", "None, because stores check every extension", "It only slows the browser down", "It only changes how pages look",
            "Extensions can request broad permissions. A harmful one can capture sign-ins and data.",
            "Install only extensions that {company} has approved."),
        Q("safe-browsing", "intermediate", "A search ad for your usual software has a web address you do not recognize. What should you do?",
            "Skip the ad and go to the vendor site you already know", "Click it, because ads are checked", "Click it if it is the top result", "Click it, but do not download anything",
            "Attackers buy ads that imitate real software to spread malware (malvertising).",
            "Use bookmarks or the approved software portal."),

        // ── Data handling (Microsoft security documentation, CISA) ──
        Q("data-handling", "basic", "You need to send a file with customer details to a coworker. What is the best way?",
            "Use the file sharing approved by {company}, and share only with them", "Email it to your personal address first", "Post it in a large chat channel", "Send it through a personal messaging app",
            "Approved tools give access controls and logs. Personal accounts and wide channels lose control of the data.",
            "Share the minimum the person needs."),
        Q("data-handling", "basic", "You emailed confidential information to the wrong person by mistake. What should you do first?",
            "Tell {reportTo} right away", "Wait to see if they reply", "Ask them to delete it and say nothing more", "Delete your sent copy",
            "Quick reports let the security team act while the damage is still small.",
            "Report it, and do not try to hide it."),
        Q("data-handling", "basic", "What is the best way to dispose of paper with confidential information?",
            "Use the secure shredding bin", "Put it in the recycling", "Tear it in half and put it in the trash", "Take it home to throw away",
            "Torn or recycled paper can be read. Shredding is how confidential paper is destroyed.",
            "Never leave confidential papers in open bins."),
        Q("data-handling", "basic", "What should you do with sensitive papers when you leave your desk for the day?",
            "Lock them in a drawer or cabinet", "Leave them in a neat pile", "Turn them face down", "Cover them with a folder",
            "A clean desk keeps information away from cleaners, visitors, and anyone passing by.",
            "Lock your screen as well."),
        Q("data-handling", "intermediate", "A coworker asks you to put a spreadsheet of employee data in your personal cloud storage to work on at home. What is best?",
            "Decline, and use storage approved by {company}", "Agree, if the link is private", "Agree, if you delete it afterward", "Agree, if you zip it with a password",
            "Personal storage is outside the company's control, so the data cannot be protected or recovered.",
            "Ask {reportTo} how to work with this data from home."),
        Q("data-handling", "intermediate", "How should you share a document with an outside partner who only needs to read it?",
            "Send a view-only link for that person that expires", "Attach the editable original to every email", "Share an edit link that anyone with the link can use", "Post it on a public page and send the address",
            "Give the least access, to the right person, for the shortest time.",
            "Review who has access when the work is done."),
        Q("data-handling", "intermediate", "Why should you collect and keep only the personal data you actually need?",
            "Less stored data means less to lose in a breach", "It makes the data easier to search", "It saves space but has no security benefit", "The rules apply only to data in the cloud",
            "Data you do not hold cannot be stolen. Minimizing data also supports privacy rules.",
            "Delete data you no longer need, following {company}'s retention policy."),
        Q("data-handling", "intermediate", "You find a document with customer personal details in a folder shared with everyone. What should you do?",
            "Report it to {reportTo}, and do not copy or share it", "Leave it, since someone else will fix it", "Move it to your own folder", "Tell the team about it in a public chat",
            "Overshared data is a risk until access is fixed. Copying or discussing it widely adds to the exposure.",
            "Note where you found it and who can see it."),

        // ── Devices (CISA, Microsoft security documentation) ──
        Q("devices", "basic", "Why should you install software and security updates promptly?",
            "Updates fix known weaknesses that attackers use", "They mostly change how things look", "They are optional if you have antivirus", "They only matter on old devices",
            "Many attacks use flaws that already have a fix. Updating closes the door.",
            "Turn on automatic updates and restart when asked."),
        Q("devices", "basic", "You find a USB drive in the parking lot. What should you do?",
            "Do not plug it in; give it to {reportTo}", "Plug it in to see whose it is", "Plug it in on a spare laptop", "Plug it in on your personal computer",
            "Attackers leave drives to be found. Plugging one in can run malware at once.",
            "Hand it in and let the security team check it."),
        Q("devices", "basic", "What should you do when you step away from your computer, even for a moment?",
            "Lock the screen", "Turn the brightness down", "Leave it on so you do not lose your place", "Open a different window",
            "Anyone nearby can use an unlocked computer as you.",
            "Press Windows key + L, or Control + Command + Q on a Mac."),
        Q("devices", "basic", "Your work laptop is lost or stolen. What should you do?",
            "Report it to {reportTo} right away", "Wait a day in case it turns up", "Report it only if it had personal files", "Buy a replacement and say nothing",
            "A quick report lets the team lock the device and protect accounts before anything is misused.",
            "Report it even if you think it was only misplaced."),
        Q("devices", "intermediate", "Why should a laptop's drive be encrypted?",
            "The data stays protected if the laptop is lost or stolen", "It makes the laptop run faster", "It stops viruses", "It makes the battery last longer",
            "Encryption makes the files unreadable without the sign-in, even if the drive is removed.",
            "Keep device encryption turned on."),
        Q("devices", "intermediate", "Why avoid installing apps from unofficial sources on a work phone?",
            "They can carry malware or spy on your data", "They always cost money", "They use more battery but are safe", "They are blocked from the internet",
            "Official stores check apps. Other sources often do not.",
            "Use the app catalog approved by {company}."),
        Q("devices", "intermediate", "A program will not run, and a website says to turn off your device's security tool first. What should you do?",
            "Do not turn it off; ask {reportTo} or IT for help", "Turn it off for a minute, then back on", "Turn it off if the site looks professional", "Uninstall the security tool",
            "Telling you to disable protection is a red flag. It is how malware gets past defenses.",
            "Ask IT for an approved way to get the program."),
        Q("devices", "intermediate", "You use your personal phone for work email. What is the best practice?",
            "Follow {company}'s rules for personal devices, including screen lock and updates", "Anything goes, because it is your own device", "Share the phone with family if you sign out first", "Skip updates to avoid problems",
            "Work data on a personal device is still company data. A lock and current updates protect it.",
            "Ask {reportTo} what is allowed on personal devices."),

        // ── Remote work (CISA, Microsoft security documentation) ──
        Q("remote-work", "basic", "What should you do about your home Wi-Fi router?",
            "Change the default admin password and use a strong Wi-Fi password", "Leave the default settings so it keeps working", "Turn off encryption for speed", "Share the password with neighbors",
            "Default router passwords are public. Anyone who gets in can see or redirect your traffic.",
            "Also keep the router's software up to date."),
        Q("remote-work", "basic", "What is a good habit when you take a work call in a public place?",
            "Keep your voice down and avoid confidential details", "Speak up so the caller can hear you", "Use the speakerphone", "Share your screen to help others follow",
            "Anyone nearby can hear what you say.",
            "Move somewhere private for sensitive calls."),
        Q("remote-work", "basic", "Who may use your work laptop at home?",
            "Only you", "Family members, if they are careful", "Friends, if you watch them", "Anyone, if they use a guest account",
            "Work devices hold company data and accounts, and are for authorized users only.",
            "Keep the laptop locked away from others when not in use."),
        Q("remote-work", "basic", "Before you share your screen in a video meeting, what should you do?",
            "Close private windows and notifications you do not want others to see", "Nothing, because screens are always private", "Share your whole desktop to be safe", "Turn off your camera",
            "Sharing shows everything on the screen, including messages and tabs.",
            "Share a single window when you can."),
        Q("remote-work", "intermediate", "When you use a VPN for work, what is true?",
            "It protects your connection to the company, but not against phishing or malware", "It makes you safe from every threat", "It lets you skip updates", "It means you can skip MFA",
            "A VPN protects data in transit. It does not stop you from clicking a bad link.",
            "Keep your other protections on while on the VPN."),
        Q("remote-work", "intermediate", "A hotel business center has a shared computer. Should you sign in to work accounts on it?",
            "No; use your own approved device", "Yes, then sign out", "Yes, in a private browser window", "Yes, if the hotel staff seem trustworthy",
            "Shared computers can have keyloggers or spyware that capture your password.",
            "Use your own device or your phone's hotspot."),
        Q("remote-work", "intermediate", "A smart speaker sits in the room during your confidential work calls. What is wise?",
            "Mute it or move it out of the room during sensitive calls", "Nothing, because it listens only for its wake word", "Ask it to delete your history afterward", "Keep it on in case you need an alarm",
            "Devices with microphones can capture sensitive conversations by accident.",
            "Take the same care with smart TVs and displays."),
        Q("remote-work", "intermediate", "A teammate wants to move a work conversation to a personal messaging app because it is faster. What should you do?",
            "Keep work conversations in the tools approved by {company}", "Move it, because the content is the same", "Move it only for short messages", "Move it if the app is encrypted",
            "Approved tools provide access control, retention, and security review. Personal apps do not.",
            "Ask {reportTo} if you need a faster approved option."),

        // ── Using AI safely (NIST AI RMF, Microsoft Responsible AI, OWASP LLM Top 10) ──
        Q("ai-use", "basic", "Before you paste text into a public AI chatbot, what should you check?",
            "That the text has no confidential or personal information", "That the text is under a certain length", "That the chatbot is popular", "Nothing, because AI tools keep everything private",
            "What you enter may be stored or used by the service. Public tools are outside company control.",
            "Use the AI tools that {company} has approved."),
        Q("ai-use", "basic", "An AI tool gives a confident statistic for a customer report. What should you do?",
            "Check it against a reliable source before using it", "Use it, because confident answers are correct", "Use it if it sounds specific", "Ask the AI if it is sure, and use its reply",
            "AI can state wrong facts convincingly. You are responsible for what you publish.",
            "Keep the source when you verify."),
        Q("ai-use", "basic", "May you upload a customer contract to a free AI tool to summarize it?",
            "No, unless {company} has approved that tool for this kind of data", "Yes, because summarizing is harmless", "Yes, if you remove the company's name", "Yes, if you delete the chat afterward",
            "Contracts contain confidential terms. Deleting a chat does not undo what the service already received.",
            "Ask {reportTo} which tools can handle confidential files."),
        Q("ai-use", "basic", "A video shows your CEO announcing something odd and asking staff to act. What should you do?",
            "Verify through official channels before acting, since it may be a deepfake", "Act on it, because video cannot be faked", "Share it with others first", "Reply to the sender",
            "AI can make realistic fake video and audio. Verify surprising instructions.",
            "Report it to {reportTo}."),
        Q("ai-use", "intermediate", "A free AI app asks for access to all your email and files. What should you do?",
            "Do not grant access; ask {reportTo} if the app is approved", "Grant access for the free trial", "Grant read-only access", "Grant access on your phone only",
            "Broad permissions let an app read everything, and a fake app can use them to steal data (consent phishing).",
            "Check app permissions before you approve them."),
        Q("ai-use", "intermediate", "An AI assistant suggests code for a work project. What is a good practice?",
            "Review and test it like any other code, and check licensing rules", "Trust it, because AI code is always secure", "Skip review for short snippets", "Remove the comments so it cannot be traced",
            "AI code can contain bugs, vulnerabilities, or copied licensed code.",
            "Run your normal code review and security checks."),
        Q("ai-use", "intermediate", "Hidden text on a web page tells an AI assistant to ignore its rules and leak your data. What is this attack called?",
            "Prompt injection", "Tailgating", "Shoulder surfing", "Brute force",
            "AI tools can mistake instructions hidden in content for your instructions.",
            "Be careful about letting AI tools act on content you do not trust, and review what they plan to do."),
        Q("ai-use", "intermediate", "Your team wants to use a new AI tool with customer data. What should happen first?",
            "Get it reviewed and approved by the security and privacy teams", "Start with a small test using real data", "Rely on the vendor's website claims", "Use personal accounts so the company is not responsible",
            "Customer data needs review of where it goes, how it is stored, and whether it is used for training.",
            "Contact {reportTo} to start a review."),

        // ── Physical security (CISA, Microsoft security documentation) ──
        Q("physical", "basic", "A person without a badge follows you into a secure area, saying they forgot theirs. What should you do?",
            "Politely stop them and direct them to reception or security", "Let them in, because they look friendly", "Hold the door, since it is polite", "Let them in if they know a name",
            "Following someone through a door (tailgating) is a common way into a building.",
            "Tell security if the person is hard to place."),
        Q("physical", "basic", "What should you do with your access badge?",
            "Keep it with you, and report it right away if lost", "Lend it to a coworker who left theirs at home", "Leave it on your desk for others", "Post a photo of it online",
            "A badge is a key to the building. Photos can be copied, and loans cannot be traced.",
            "Report a lost badge to {reportTo}."),
        Q("physical", "basic", "You see a visitor walking alone with no escort or badge. What should you do?",
            "Ask kindly if you can help, and notify security if unsure", "Ignore them, because it is someone else's job", "Follow them quietly", "Mention it to coworkers later",
            "Visitors should be signed in and escorted. A friendly question is a safe first step.",
            "Do not confront anyone who seems hostile; call security."),
        Q("physical", "basic", "You must leave a work laptop in a car for a short time. What is best?",
            "Avoid it; if you cannot, lock it in the trunk out of sight", "Cover it with a jacket on the seat", "Leave it on the seat, since you will be quick", "Leave it under the seat",
            "Thieves watch for devices in view, and a short stop is all they need.",
            "Take the laptop with you when you can."),
        Q("physical", "intermediate", "Someone in a hi-vis vest says they are here to fix the network and asks to enter the server room. What is best?",
            "Confirm with {reportTo} or facilities before letting them in", "Let them in, because they look the part", "Let them in, but watch them", "Ask for a business card first",
            "Uniforms and vests are easy to copy. Verify the visit with someone who knows.",
            "Keep the door locked until it is confirmed."),
        Q("physical", "intermediate", "What is 'shoulder surfing'?",
            "Watching someone's screen or keyboard to see information", "Using a wireless network without permission", "Guessing passwords with software", "Sending fake emails",
            "Anyone near you can see what you type or read, especially in public.",
            "Use a privacy screen and notice who is nearby."),
        Q("physical", "intermediate", "You send a confidential document to a shared printer. What should you do?",
            "Collect it at once, or use a badge-release print option", "Leave it, since the printer is in the office", "Collect it later in the day", "Print two copies in case one is lost",
            "Papers left at printers are easy to read or take.",
            "Shred anything you no longer need."),
        Q("physical", "intermediate", "At a conference, a stranger asks to charge their phone from your work laptop's USB port. What is best?",
            "Decline politely", "Allow it, since it is just charging", "Allow it if the phone looks new", "Allow it with the laptop locked",
            "A USB port can carry data or malware as well as power.",
            "Offer a wall charger instead."),

        // ── Reporting incidents (CISA, Microsoft security documentation) ──
        Q("reporting", "basic", "You think you clicked a phishing link. What should you do first?",
            "Report it right away to {reportTo}, even if nothing seems wrong", "Wait to see whether anything happens", "Restart your computer and say nothing", "Delete the email and keep working",
            "The sooner it is reported, the less harm it can do. Quick reports are valued, not punished.",
            "Tell them what you clicked and when."),
        Q("reporting", "basic", "Who should hear about a suspected security problem first?",
            "{reportTo}, as soon as possible", "Only your manager", "The sender of the message", "A friend who works in IT somewhere else",
            "The security team can act, and knows how to protect the company.",
            "Save the contact details for {reportTo} where you can find them."),
        Q("reporting", "basic", "You made a mistake that might cause a security issue. Which approach is best?",
            "Report it quickly, because early reports reduce harm", "Fix it quietly to avoid trouble", "Wait until your next review", "Report it only if it is serious",
            "Problems grow when they are hidden. Early reports give the team more options.",
            "Describe what happened and what you have done so far."),
        Q("reporting", "basic", "Should you report something that only seems suspicious, if you are not sure it is a threat?",
            "Yes; it is better to check than to ignore it", "No; only confirmed incidents should be reported", "No; you might get in trouble", "Only if more than one person noticed",
            "Security teams would rather review a false alarm than miss a real attack.",
            "Send what you saw to {reportTo}."),
        Q("reporting", "intermediate", "What helps most when you report a suspicious email?",
            "The original message, sent as an attachment, and what you did with it", "A screenshot of just the subject line", "Only your guess about who sent it", "A summary written from memory a week later",
            "The original keeps technical details the team needs to trace it.",
            "Use the report button if your email has one."),
        Q("reporting", "intermediate", "A colleague tells you they clicked a bad link but asks you not to tell anyone. What should you do?",
            "Encourage them to report it, and report it yourself if they do not", "Keep quiet to protect them", "Wait a week to see what happens", "Delete the email for them",
            "Waiting gives attackers time. Reporting protects your colleague and the company.",
            "Contact {reportTo} with what you know."),
        Q("reporting", "intermediate", "You suspect a coworker is bypassing security controls. What is best?",
            "Report it through the proper channel, such as {reportTo}", "Confront them in a team meeting", "Post about it in a public channel", "Ignore it",
            "Concerns are handled best by the people trained to assess them, fairly and privately.",
            "Stick to facts about what you saw."),
        Q("reporting", "intermediate", "A vendor tells you their systems were breached and may include your company's data. What should you do?",
            "Pass it to {reportTo} straight away", "Wait for the vendor to follow up", "Tell the affected customers yourself", "Assume the vendor will handle it",
            "A vendor breach can affect your company. The right teams must assess it and decide what to say and to whom.",
            "Share the vendor's message as it was sent.")
    };
}
