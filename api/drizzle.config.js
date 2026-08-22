import "dotenv/config";
export default {
    schema: "./src/db/schema.ts",
    out: "./drizzle",
    dialect: "sqlite",
    dbCredentials: {
        url: "./data.sqlite",
    },
};
//# sourceMappingURL=drizzle.config.js.map