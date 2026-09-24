# Build stage
FROM node:22-alpine AS build
WORKDIR /app

# Copy package descriptors and install dependencies
COPY package*.json ./
RUN npm ci --legacy-peer-deps

# Copy application source and build
COPY . .
RUN npm run build

# Runtime stage
FROM node:22-alpine AS final
WORKDIR /app

ENV NODE_ENV=production \
    PORT=3000 \
    HOST=0.0.0.0

# Copy built application and node modules with unprivileged user ownership
COPY --from=build --chown=node:node /app /app

USER node
EXPOSE 3000

CMD ["npx", "vite", "preview", "--host", "0.0.0.0", "--port", "3000"]
