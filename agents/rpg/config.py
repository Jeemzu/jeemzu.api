import os
from dotenv import load_dotenv
from pydantic import SecretStr

load_dotenv()

OPENAI_API_KEY = SecretStr(os.getenv("OPENAI_API_KEY", ""))
OPENAI_MODEL_NARRATOR = os.getenv("OPENAI_MODEL_NARRATOR", "gpt-4o")
OPENAI_MODEL_FAST = os.getenv("OPENAI_MODEL_FAST", "gpt-4o-mini")
DATABASE_URL = os.getenv("DATABASE_URL", "postgresql://jeemzu:jeemzu_dev_password@localhost:5432/jeemzu_rpg")
