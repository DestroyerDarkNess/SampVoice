# Use a slim Python image for minimum size
FROM python:3.11-slim

# Environment variables to optimize Python for containers
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

# Metadata
LABEL maintainer="UniversalVoiceChat"
LABEL description="WebSocket Relay Server for SA:MP proximity voice chat"

# Set working directory
WORKDIR /app

# Copy requirements and install
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy the server script
COPY relay.py .

# Expose the default relay port
EXPOSE 8000

# Run the relay
CMD ["python", "relay.py"]
