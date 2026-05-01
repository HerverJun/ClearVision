FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY virtual_plc_modbus.py .

EXPOSE 1502

CMD ["python", "virtual_plc_modbus.py", "--host", "0.0.0.0", "--port", "1502"]
