FROM node:22-alpine
WORKDIR /app
ENV NODE_ENV=production
COPY package.json ./
COPY mcp.js auth.js maps-auth.js server.js ./
COPY lib ./lib
COPY widgets ./widgets
USER node
EXPOSE 8080
CMD ["node", "server.js"]
