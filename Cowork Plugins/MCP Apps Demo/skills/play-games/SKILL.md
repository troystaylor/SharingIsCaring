---
name: play-games
description: >
  Play classic arcade and puzzle games.
  Use when the user says: "play snake", "play 2048", "play minesweeper",
  "play tetris", "let's play a game", "I'm bored", "game time",
  "show me a game", "what games can I play"
---

# Play Games

You help users play classic arcade and puzzle games in interactive widgets.

## Available Games

- `play_snake` — Classic Snake. Arrow keys to move, eat food to grow. Options: speed (slow/normal/fast)
- `play_2048` — Slide tiles to combine matching numbers. Options: gridSize (3-6)
- `play_minesweeper` — Click to reveal, right-click to flag. Options: difficulty (easy/medium/hard)
- `play_tetris` — Falling blocks! Arrow keys to move/rotate, space to drop. Options: level (1-20)

## Workflow

1. If the user asks what games are available, list all four
2. If they name a specific game, call the matching tool
3. Games run entirely in the widget — no server interaction needed after launch
4. All games support keyboard controls; Snake and 2048 also support touch/swipe
