# order-pipeline

An event-driven order processing pipeline built with .NET, Kafka, PostgreSQL, and Azure Service Bus.

Orders come in through a REST API, get saved to PostgreSQL, and published to Kafka. A background consumer picks them up, processes them, updates the order status, and publishes a fulfilment event to Azure Service Bus. The whole thing runs locally with Docker.

## How it works

There are three projects.

**OrderPipeline.Api** is an ASP.NET Core 10 API that accepts order requests. When an order arrives, it saves it to PostgreSQL and publishes an event to a Kafka topic called `orders`. The API returns immediately. Processing happens asynchronously.

**OrderPipeline.Consumer** is a .NET Worker Service that runs continuously. It reads from the Kafka `orders` topic, updates the order status to Processing, then marks it as Fulfilled in PostgreSQL. Once fulfilled, it publishes a fulfilment event to an Azure Service Bus queue.

**OrderPipeline.Core** holds the shared domain models and interfaces. Order, OrderItem, OrderEvent, and the repository and publisher contracts live here.

## What a real run looks like

POST an order to the API:

```bash
curl -X POST http://localhost:5125/api/orders \
  -H "Content-Type: application/json" \
  -d "{\"customerName\": \"John Smith\", \"customerEmail\": \"john@example.com\", \"items\": [{\"productName\": \"Laptop\", \"quantity\": 1, \"unitPrice\": 999.99}]}"
```

The API log shows:

```
Order e6e20d66 created in database
Order e6e20d66 published to Kafka topic orders at offset 1
```

The Consumer log shows:

```
Consumed message from partition [0] at offset 1
Processing order e6e20d66 for customer John Smith
Fulfilment event published to Service Bus for order e6e20d66
Order e6e20d66 fulfilled successfully
```

## Stack

- .NET 10
- ASP.NET Core 10 (API)
- .NET Worker Service (Consumer)
- Kafka via Confluent.Kafka
- PostgreSQL via Npgsql and EF Core 9
- Azure Service Bus via Azure.Messaging.ServiceBus
- Serilog
- Docker and Docker Compose

## Running locally

You need .NET 10 and Docker Desktop.

Clone the repo and start the infrastructure:

```bash
git clone https://github.com/aftabkh4n/order-pipeline.git
cd order-pipeline
docker-compose up -d
```

This starts PostgreSQL, Kafka, Zookeeper, Kafka UI, and the Azure Service Bus emulator.

Copy the example config in both projects:

```bash
cp OrderPipeline.Api/appsettings.example.json OrderPipeline.Api/appsettings.json
cp OrderPipeline.Consumer/appsettings.example.json OrderPipeline.Consumer/appsettings.json
```

Start the API in one terminal:

```bash
cd OrderPipeline.Api
dotnet run
```

Start the Consumer in another terminal:

```bash
cd OrderPipeline.Consumer
dotnet run
```

Send a test order:

```bash
curl -X POST http://localhost:5125/api/orders \
  -H "Content-Type: application/json" \
  -d "{\"customerName\": \"Test User\", \"customerEmail\": \"test@example.com\", \"items\": [{\"productName\": \"Test Product\", \"quantity\": 1, \"unitPrice\": 49.99}]}"
```

## API endpoints

```
POST  /api/orders              Create a new order
GET   /api/orders              List all orders
GET   /api/orders/{id}         Get a specific order
PATCH /api/orders/{id}/status  Update order status
GET   /health                  Health check
```

## Infrastructure

| Service | Port | Purpose |
| --- | --- | --- |
| PostgreSQL | 5434 | Order storage |
| Kafka | 9092 | Order event streaming |
| Kafka UI | 8080 | Kafka monitoring |
| Service Bus Emulator | 5672 | Fulfilment event queue |

## Contributing

Pull requests welcome. Open an issue if you find a bug or want to suggest an improvement.

---

Built with .NET 10, Kafka, and Azure Service Bus. MIT licence.