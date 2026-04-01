FROM node:20-alpine

WORKDIR /app

COPY package.json ./
RUN npm install --omit=dev

COPY src ./src

ENV NODE_ENV=production
ENV PORT=3010
ENV BRIDGE_LOG_LEVEL=info

EXPOSE 3010

CMD ["node", "src/index.mjs"]
