/**
 * Global error handling middleware
 */

const env = require('../config/env');

const errorHandler = (err, req, res, next) => {
  console.error('Error:', err.message);

  // Mongoose validation error
  if (err.name === 'ValidationError') {
    const messages = Object.values(err.errors).map((e) => e.message);
    return res.status(400).json({
      error: 'Validation Error',
      messages,
    });
  }

  // Malformed id (e.g. GET /api/items/not-an-id): a client error, not a 500.
  // The message is generic so internal details (ObjectId, model names) don't leak.
  if (err.name === 'CastError') {
    return res.status(400).json({
      error: `Invalid ${err.path === '_id' ? 'id' : err.path} format`,
    });
  }

  // Mongoose duplicate key error
  if (err.code === 11000) {
    const field = Object.keys(err.keyValue)[0];
    return res.status(409).json({
      error: 'Duplicate Error',
      message: `${field} already exists`,
    });
  }

  // JWT errors
  if (err.name === 'JsonWebTokenError') {
    return res.status(401).json({
      error: 'Invalid token',
    });
  }

  if (err.name === 'TokenExpiredError') {
    return res.status(401).json({
      error: 'Token expired',
    });
  }

  // Default error
  const statusCode = err.statusCode || 500;
  res.status(statusCode).json({
    error: err.message || 'Internal Server Error',
    ...(env.isDev() && { stack: err.stack }),
  });
};

module.exports = errorHandler;
