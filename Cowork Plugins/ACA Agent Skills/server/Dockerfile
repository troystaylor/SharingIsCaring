FROM node:22-slim
WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev
COPY . .
EXPOSE 8080
ENV PORT=8080
CMD ["node", "index.js"]
