import os
from dotenv import load_dotenv
from pydantic import SecretStr

load_dotenv()

OPENAI_API_KEY = SecretStr(os.getenv("OPENAI_API_KEY", ""))
OPENAI_MODEL = os.getenv("OPENAI_MODEL", "gpt-4o-mini")
DOTNET_API_URL = os.getenv("DOTNET_API_URL", "http://localhost:5000/api")
TAVILY_API_KEY = os.getenv("TAVILY_API_KEY", "")
