import os
from dotenv import load_dotenv
from pydantic import SecretStr

load_dotenv()

OPENAI_API_KEY = SecretStr(os.getenv("OPENAI_API_KEY", ""))

# The planner has to get arithmetic and the op schema exactly right, and runs far
# less often than routing, so it is the only node that pays for the full model.
OPENAI_MODEL_PLANNER = os.getenv("OPENAI_MODEL_PLANNER", "gpt-4.1")
OPENAI_MODEL_FAST = os.getenv("OPENAI_MODEL_FAST", "gpt-4.1-mini")

DOTNET_API_URL = os.getenv("DOTNET_API_URL", "http://localhost:5050/api")
INTERNAL_API_KEY = os.getenv("INTERNAL_API_KEY", "")
