# Financial Bot API

This project is a Financial Bot API built with Python and the Flask framework. It uses SQLAlchemy for database operations against a PostgreSQL database, and it integrates with the Google Gemini API for natural language processing.

## Purpose

The main purpose of this API is to provide a backend for a financial bot. The bot can be used to track expenses, manage budgets, and provide financial insights. The API exposes a set of endpoints for creating, retrieving, updating, and deleting financial data.

## Setup

To get started with this project, you'll need to have Python 3 and PostgreSQL installed on your system. You'll also need to create a virtual environment and install the required packages.

1. **Clone the repository:**

   ```bash
   git clone https://github.com/your-username/financial-bot-api.git
   ```

2. **Create a virtual environment:**

   ```bash
   python3 -m venv venv
   ```

3. **Activate the virtual environment:**

   ```bash
   source venv/bin/activate
   ```

4. **Install the required packages:**

   ```bash
   pip install -r requirements.txt
   ```

5. **Set up the database:**

   - Make sure you have PostgreSQL installed and running.
   - Create a new database for the project.
   - Set the `DATABASE_URL` environment variable to the connection string for your database.

6. **Set up the Gemini API:**

   - Get an API key from Google AI Studio.
   - Set the `GEMINI_API_KEY` environment variable to your API key.

## Usage

To run the application, you can use the following command:

```bash
gunicorn app:app
```

This will start the development server at `http://127.0.0.1:8000`. You can then use a tool like `curl` or Postman to send requests to the API.

### Endpoints

The API exposes the following endpoints:

- `GET /`: The home route of the Flask application.
- `POST /webhook-automate`: The main route that receives notifications, makes two calls to the Gemini API (for extraction and categorization), and saves the transaction in the database.
- `POST /webhook-whatsapp`: A placeholder for handling WhatsApp webhooks.

### Admin Endpoints

The API also exposes the following admin endpoints:

- `GET /admin/setup-database`: Sets up the database schema.
- `GET /admin/populate-global-categories`: Populates the database with global categories.
- `GET /admin/setup-user-data`: Sets up a dummy user for testing.
