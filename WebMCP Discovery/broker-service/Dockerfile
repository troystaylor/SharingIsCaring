# --- Build stage ---
FROM node:18-slim AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY tsconfig.json ./
COPY src/ ./src/
RUN npm run build

# --- Runtime stage ---
FROM mcr.microsoft.com/playwright:v1.58.2-jammy
WORKDIR /app
COPY package*.json ./
RUN npm ci --only=production
COPY --from=build /app/dist ./dist/

# Create non-root user for security
RUN useradd -m appuser && chown -R appuser:appuser /app
USER appuser

# Expose port
EXPOSE 3000

# Start the application
CMD ["node", "dist/index.js"]

