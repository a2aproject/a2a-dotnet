# Semantic Kernel Agent Sample
This is a work in progress and only the steps listed below are working.

## Getting Started

### Prerequisites

*   .NET 9 SDK or later

### Building the Solution

1.  Clone the repository:
    ```bash
    git clone https://github.com/a2aproject/a2a-dotnet
    cd a2a-dotnet
    ```
2.  Build the solution using the .NET CLI:
    ```bash
    dotnet build
    ```

## Testing with a Semantic Kernel Agent

1. Launch the `SemanticKernelAgent` sample server
    ```bash
    cd samples\SemanticKernelAgent
    dotnet run
    ```
2. Launch the `Client` passing the following arguments `http://localhost:5000 TravelPlanner`.
    ```bash
    cd samples\Client
    dotnet run http://localhost:5000 TravelPlanner
    ```
3. The client will send requests to the `SemanticKernelAgent` sample server
    ```
    You: Hi, I live in Korea. I want to travel to Dublin for a week and visit the sites. What should I visit and how much money will I need?
    Debug: Started activity 00-21cbaf6cba101313a71ff23d967aab33-0688a522a002a0b6-01 of kind Client
    TravelPlanner: That’s a wonderful trip! Dublin is full of history, culture, and fun experiences. Here’s a recommended one-week itinerary with top sites and a budget estimate for your visit from Korea.
    ```
