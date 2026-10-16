# ArtAround

ArtAround is a web app for guided museum tours.  of two frontends that share one backend:

- **Marketplace**: authors write descriptions of artworks ("items") in 3 tones × 3 lengths and arrange them into tours ("visits"). Items can be bought with an in-app wallet.
- **Navigator**: visitors pick a museum and a visit, then follow it stop by stop on their phone, with text-to-speech, voice commands and a museum map.

Visits can also be run as **group visits**: a host shares a session code, participants join and follow the tour in sync, with a group chat, questions along the way and a final quiz.

Built by Federico Serra, for the Web Technologies course at the University of Bologna.

## Running

*Note: every server start resets the database with demo data (two museums, sample visits and test accounts). Changes made through the UI are not kept.*

**Requirements:** Node.js 22 and a MongoDB instance.

**1. Set up the `.env` file in the project root:**
```
NODE_ENV=development
PORT=8000
DB_HOST=localhost:27017
DB_USER=your_mongo_user
DB_PASS=your_mongo_password
DB_NAME=artaround
JWT_SECRET=any_long_random_string
JWT_EXPIRES_IN=7d
```

**2. Build the navigator:**
```bash
cd frontend-navigator && npm install && npm run build
```

**3. Start the server:**
```bash
npm install && npm start
```

- Navigator: http://localhost:8000
- Marketplace: http://localhost:8000/marketplace
- API: http://localhost:8000/api

Demo login: `visitatore1` or `autore1`, password `12345678`.

## Architecture

### Backend

Node.js + Express REST API with MongoDB persistence and real-time sync via Socket.IO.

- **Express**: HTTP server, JSON API under `/api`. Also serves both frontends and the uploaded images
- **MongoDB + Mongoose**: stores museums, artworks, items, visits, sessions and users
- **JWT (jsonwebtoken)**: login returns a token, checked by the `requireAuth` middleware. Passwords are hashed with bcrypt
- **Zod**: validates request bodies before they reach the controllers
- **Socket.IO**: keeps group visits in sync. Every action goes through the REST API first; the server then pushes the update to everyone in the session
- **Seed script**: loads museums from `data/museums/*.json` and creates the demo users, items and visits

Request flow: `route → validate (Zod) → requireAuth (JWT) → controller → model → JSON response`

### Marketplace

Plain HTML, CSS and JavaScript, no framework. Served by Express under `/marketplace`. Pages: homepage, login and register, visit list, visit editor, item editor, my items and account.

### Navigator

React 18 + Vite single-page app, mobile-first. Built into `dist/` and served by Express on every route not used by the API, the marketplace or the uploads. Uses the same JWT and the same API as the marketplace. Italian and English via i18next.

## Structure

```
artaround/
├── .env                       # Environment config
├── data/
│   └── museums/               # Museum config files (JSON, loaded by the seed)
├── uploads/                   # Images, served at /uploads
├── src/                       # Backend (Node + Express + Mongoose)
│   ├── index.js               # Entry point: wires API, marketplace, navigator
│   ├── config/                # Database and env setup
│   ├── models/                # User, Museum, Content, Item, Visit, Session
│   ├── controllers/           # Request handlers
│   ├── routes/                # API route definitions
│   ├── schemas/               # Zod validation schemas
│   ├── middleware/            # Auth, validation, error handling
│   └── services/              # Socket.IO sync, wallet purchases
├── frontend/marketplace/      # Marketplace: pages, scripts, stylesheets
├── frontend-navigator/        # Navigator (React + Vite)
│   ├── src/                   # Pages, components, styles, translations
│   └── vite.config.js         # Dev server proxy to :8000
└── scripts/
    ├── seed.js                # Resets the demo database
    └── load-museum.js         # Loads museum config files
```
