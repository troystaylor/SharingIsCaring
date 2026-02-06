/**
 * Logger - File-based logging for Power Agent Tray
 * Writes to a rotating log file in the user's app data directory
 */

import * as fs from "fs";
import * as path from "path";
import { app } from "electron";

export type LogLevel = "debug" | "info" | "warn" | "error";

const LEVEL_ORDER: Record<LogLevel, number> = {
  debug: 0,
  info: 1,
  warn: 2,
  error: 3,
};

const MAX_LOG_SIZE = 5 * 1024 * 1024; // 5 MB
const MAX_LOG_FILES = 3;

class Logger {
  private logDir: string;
  private logFile: string;
  private minLevel: LogLevel = "info";
  private stream: fs.WriteStream | null = null;

  constructor() {
    // Use Electron's userData path when available, fallback to cwd
    try {
      this.logDir = path.join(app.getPath("userData"), "logs");
    } catch {
      this.logDir = path.join(process.cwd(), "logs");
    }
    this.logFile = path.join(this.logDir, "power-agent-tray.log");
  }

  /**
   * Initialize the logger - creates log directory and opens write stream
   */
  initialize(level?: LogLevel): void {
    if (level) this.minLevel = level;

    if (!fs.existsSync(this.logDir)) {
      fs.mkdirSync(this.logDir, { recursive: true });
    }

    this.rotateIfNeeded();
    this.stream = fs.createWriteStream(this.logFile, { flags: "a" });

    // Redirect console methods to also write to log file
    const originalLog = console.log;
    const originalError = console.error;
    const originalWarn = console.warn;

    console.log = (...args: unknown[]) => {
      originalLog(...args);
      this.info(args.map(String).join(" "));
    };

    console.error = (...args: unknown[]) => {
      originalError(...args);
      this.error(args.map(String).join(" "));
    };

    console.warn = (...args: unknown[]) => {
      originalWarn(...args);
      this.warn(args.map(String).join(" "));
    };

    this.write("info", "Logger initialized");
  }

  debug(message: string): void {
    this.write("debug", message);
  }

  info(message: string): void {
    this.write("info", message);
  }

  warn(message: string): void {
    this.write("warn", message);
  }

  error(message: string): void {
    this.write("error", message);
  }

  /**
   * Write a log entry to the file
   */
  private write(level: LogLevel, message: string): void {
    if (LEVEL_ORDER[level] < LEVEL_ORDER[this.minLevel]) return;
    if (!this.stream) return;

    const timestamp = new Date().toISOString();
    const line = `${timestamp} [${level.toUpperCase().padEnd(5)}] ${message}\n`;

    try {
      this.stream.write(line);
    } catch {
      // Silently fail \u2014 don't recurse into error logging
    }
  }

  /**
   * Rotate log file if it exceeds MAX_LOG_SIZE
   */
  private rotateIfNeeded(): void {
    try {
      if (!fs.existsSync(this.logFile)) return;

      const stats = fs.statSync(this.logFile);
      if (stats.size < MAX_LOG_SIZE) return;

      // Shift existing rotated logs
      for (let i = MAX_LOG_FILES - 1; i >= 1; i--) {
        const from = `${this.logFile}.${i}`;
        const to = `${this.logFile}.${i + 1}`;
        if (fs.existsSync(from)) {
          if (i + 1 >= MAX_LOG_FILES) {
            fs.unlinkSync(from);
          } else {
            fs.renameSync(from, to);
          }
        }
      }

      // Rotate current log
      fs.renameSync(this.logFile, `${this.logFile}.1`);
    } catch {
      // Best-effort rotation
    }
  }

  /**
   * Get the log directory path
   */
  getLogDir(): string {
    return this.logDir;
  }

  /**
   * Close the log stream
   */
  close(): void {
    this.stream?.end();
    this.stream = null;
  }
}

export const logger = new Logger();
