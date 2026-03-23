import asyncio
import struct
import math
import time
import os
from aiohttp import web, WSCloseCode

# ============================================================
# Universal Voice Chat - Aiohttp Relay Server
# Highly stable for Render.com, Railway, etc.
# ============================================================

MAX_DISTANCE = 40.0
TIMEOUT = 5.0
PORT = int(os.environ.get('PORT', 8000))

# rooms["server_ip:port"] = { player_id: { "ws": ws, "x": 0, "y": 0, "z": 0, "last": time } }
rooms = {}

async def websocket_handler(request):
    """Handles both WebSocket voice traffic and Render/LB health checks."""
    # Enable heartbeat (Ping/Pong) every 30 seconds to prevent Render proxy timeouts
    ws = web.WebSocketResponse(heartbeat=30.0)
    
    # Render/Health checks often send a GET or HEAD without WebSocket headers.
    # We check if it's a valid WebSocket request first.
    if request.headers.get("Upgrade", "").lower() != "websocket":
        return web.Response(text="Relay is Up")

    await ws.prepare(request)
    player_id = None
    server_key = None

    try:
        async for msg in ws:
            if msg.type == web.WSMsgType.BINARY:
                data = msg.data
                if len(data) < 17: continue

                ip_len = data[0]
                if len(data) < 1 + ip_len + 16: continue

                server_key = data[1:1+ip_len].decode('ascii')
                offset = 1 + ip_len

                player_id, x, y, z = struct.unpack_from("<Ifff", data, offset)
                offset += 16
                payload = data[offset:]

                if server_key not in rooms:
                    rooms[server_key] = {}

                rooms[server_key][player_id] = {
                    "ws": ws,
                    "x": x, "y": y, "z": z,
                    "last": time.time()
                }

                if len(payload) == 0: continue

                # Position-based Broadcast
                now = time.time()
                to_remove = []

                for tid, c in rooms[server_key].items():
                    if now - c["last"] > TIMEOUT:
                        to_remove.append(tid)
                        continue
                    if tid == player_id:
                        continue

                    dist = math.sqrt((c["x"]-x)**2 + (c["y"]-y)**2 + (c["z"]-z)**2)
                    if dist <= MAX_DISTANCE:
                        fwd = struct.pack("<Ifff", player_id, x, y, z) + payload
                        try:
                            await c["ws"].send_bytes(fwd)
                        except:
                            to_remove.append(tid)

                for tid in to_remove:
                    rooms[server_key].pop(tid, None)

            elif msg.type == web.WSMsgType.ERROR:
                print(f'[Relay] Connection closed with error: {ws.exception()}')

    finally:
        if server_key and player_id is not None and server_key in rooms:
            rooms[server_key].pop(player_id, None)
            if not rooms[server_key]:
                del rooms[server_key]
    
    return ws

def main():
    app = web.Application()
    app.add_routes([
        web.get('/', websocket_handler),
    ])
    
    print(f"[Relay] Aiohttp Voice Relay starting on 0.0.0.0:{PORT}")
    web.run_app(app, host='0.0.0.0', port=PORT, access_log=None)

if __name__ == '__main__':
    main()
