FROM golang:1.23-alpine AS builder
WORKDIR /src
COPY backend/ ./backend/
WORKDIR /src/backend
RUN go mod tidy && CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/tongyin .

FROM alpine:3.21
RUN apk add --no-cache ca-certificates && addgroup -S ty && adduser -S ty -G ty && mkdir /data && chown ty:ty /data
COPY --from=builder /out/tongyin /app/tongyin
COPY admin /app/admin
WORKDIR /app
USER ty
ENV LISTEN_ADDR=:8080 DATA_DIR=/data ADMIN_DIR=/app/admin
EXPOSE 8080
CMD ["/app/tongyin"]
