using System;
using System.Collections.Generic;
using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArtAround.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        private static readonly string[] TablesWithUpdatedAt = ["museums", "contents", "users", "items", "sessions"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:activity_action", "author,complex,details,exit,joined,left,map,more,shorter,simpler,tellLess,tellMore,tooSimple,year")
                .Annotation("Npgsql:Enum:answer_type", "multiple-choice,open")
                .Annotation("Npgsql:Enum:block_type", "artwork,questions")
                .Annotation("Npgsql:Enum:content_type", "Artist,Artwork,Movement,Place")
                .Annotation("Npgsql:Enum:language", "en,it")
                .Annotation("Npgsql:Enum:license", "CC-BY,CC-BY-NC,CC-BY-SA,Copyright,Public Domain")
                .Annotation("Npgsql:Enum:poi_type", "bar,entrance,exit,shop,stairs,toilet")
                .Annotation("Npgsql:Enum:tone", "complex,easy,medium")
                .Annotation("Npgsql:Enum:visit_length", "deep,normal,quick")
                .Annotation("Npgsql:Enum:visit_type", "standard,synchronized");

            migrationBuilder.CreateTable(
                name: "museums",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_museums", x => x.id);
                    table.CheckConstraint("slug_has_only_allowed_chars", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    username = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    language = table.Column<Language>(type: "language", nullable: false, defaultValue: Language.It),
                    wallet_balance = table.Column<int>(type: "integer", nullable: false, defaultValue: 100),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("users_username_min_len", "char_length(username) >= 3");
                    table.CheckConstraint("users_username_trimmed", "username = btrim(username)");
                    table.CheckConstraint("users_wallet_nonnegative", "wallet_balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "floor_plans",
                columns: table => new
                {
                    museum_id = table.Column<long>(type: "bigint", nullable: false),
                    floor = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    north = table.Column<double>(type: "double precision", nullable: false),
                    south = table.Column<double>(type: "double precision", nullable: false),
                    east = table.Column<double>(type: "double precision", nullable: false),
                    west = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_floor_plans", x => new { x.museum_id, x.floor });
                    table.CheckConstraint("floor_plans_lat_order", "south < north");
                    table.CheckConstraint("floor_plans_lat_range", "south >= -90 AND north <= 90");
                    table.CheckConstraint("floor_plans_lng_order", "west < east");
                    table.CheckConstraint("floor_plans_lng_range", "west >= -180 AND east <= 180");
                    table.ForeignKey(
                        name: "fk_floor_plans_museums_museum_id",
                        column: x => x.museum_id,
                        principalTable: "museums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    museum_id = table.Column<long>(type: "bigint", nullable: false),
                    creator_id = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<VisitType>(type: "visit_type", nullable: false, defaultValue: VisitType.Standard),
                    length = table.Column<VisitLength>(type: "visit_length", nullable: false, defaultValue: VisitLength.Normal),
                    is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    view_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visits", x => x.id);
                    table.CheckConstraint("visits_view_count_nonnegative", "view_count >= 0");
                    table.ForeignKey(
                        name: "fk_visits_museums_museum_id",
                        column: x => x.museum_id,
                        principalTable: "museums",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_visits_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "contents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    universal_id = table.Column<string>(type: "text", nullable: false),
                    museum_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<ContentType>(type: "content_type", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    author = table.Column<string>(type: "text", nullable: true),
                    year = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    floor = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contents", x => x.id);
                    table.CheckConstraint("contents_lat_range", "lat BETWEEN -90 AND 90");
                    table.CheckConstraint("contents_lng_range", "lng BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_contents_floor_plans_museum_id_floor",
                        columns: x => new { x.museum_id, x.floor },
                        principalTable: "floor_plans",
                        principalColumns: new[] { "museum_id", "floor" });
                    table.ForeignKey(
                        name: "fk_contents_museums_museum_id",
                        column: x => x.museum_id,
                        principalTable: "museums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "points_of_interest",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    museum_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<PoiType>(type: "poi_type", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    lat = table.Column<double>(type: "double precision", nullable: false),
                    lng = table.Column<double>(type: "double precision", nullable: false),
                    floor = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_points_of_interest", x => x.id);
                    table.CheckConstraint("points_of_interest_lat_range", "lat BETWEEN -90 AND 90");
                    table.CheckConstraint("points_of_interest_lng_range", "lng BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_points_of_interest_floor_plans_museum_id_floor",
                        columns: x => new { x.museum_id, x.floor },
                        principalTable: "floor_plans",
                        principalColumns: new[] { "museum_id", "floor" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_points_of_interest_museums_museum_id",
                        column: x => x.museum_id,
                        principalTable: "museums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_visits",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    visit_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_visits", x => new { x.user_id, x.visit_id });
                    table.ForeignKey(
                        name: "fk_saved_visits_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_visits_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    owner_id = table.Column<long>(type: "bigint", nullable: false),
                    visit_id = table.Column<long>(type: "bigint", nullable: false),
                    current_item_index = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_step_index = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    quiz_started = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.CheckConstraint("sessions_code_upper", "code = upper(code)");
                    table.CheckConstraint("sessions_ended_iff_inactive", "is_active = (ended_at IS NULL)");
                    table.ForeignKey(
                        name: "fk_sessions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_sessions_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_blocks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    visit_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<BlockType>(type: "block_type", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_visit_blocks_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_quiz_questions",
                columns: table => new
                {
                    visit_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    question = table.Column<string>(type: "text", nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_quiz_questions", x => new { x.visit_id, x.position });
                    table.CheckConstraint("visit_quiz_questions_correct_index_in_range", "correct_index >= 0 AND correct_index < cardinality(options)");
                    table.ForeignKey(
                        name: "fk_visit_quiz_questions_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    content_id = table.Column<long>(type: "bigint", nullable: false),
                    creator_id = table.Column<long>(type: "bigint", nullable: false),
                    target_audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    license = table.Column<License>(type: "license", nullable: false, defaultValue: License.CcBy),
                    is_public = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.CheckConstraint("items_price_nonnegative", "price >= 0");
                    table.ForeignKey(
                        name: "fk_items_contents_content_id",
                        column: x => x.content_id,
                        principalTable: "contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_items_users_creator_id",
                        column: x => x.creator_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "session_activities",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    action = table.Column<ActivityAction>(type: "activity_action", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_activities_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_activities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "session_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_messages", x => x.id);
                    table.CheckConstraint("session_messages_text_length", "char_length(text) BETWEEN 1 AND 500");
                    table.ForeignKey(
                        name: "fk_session_messages_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_messages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "session_participants",
                columns: table => new
                {
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    quiz_score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_participants", x => new { x.session_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_session_participants_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visit_block_questions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    block_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    prompt = table.Column<string>(type: "text", nullable: false),
                    answer_type = table.Column<AnswerType>(type: "answer_type", nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    correct_index = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_block_questions", x => x.id);
                    table.CheckConstraint("visit_block_questions_correct_index_in_range", "correct_index IS NULL OR (correct_index >= 0 AND correct_index < cardinality(options))");
                    table.ForeignKey(
                        name: "fk_visit_block_questions_visit_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "visit_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_associated_contents",
                columns: table => new
                {
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    content_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_associated_contents", x => new { x.item_id, x.content_id });
                    table.ForeignKey(
                        name: "fk_item_associated_contents_contents_content_id",
                        column: x => x.content_id,
                        principalTable: "contents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_item_associated_contents_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "item_texts",
                columns: table => new
                {
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    tone = table.Column<Tone>(type: "tone", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_texts", x => new { x.item_id, x.tone });
                    table.ForeignKey(
                        name: "fk_item_texts_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchases",
                columns: table => new
                {
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    price_paid = table.Column<int>(type: "integer", nullable: false),
                    purchased_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchases", x => new { x.user_id, x.item_id });
                    table.CheckConstraint("purchases_price_paid_nonnegative", "price_paid >= 0");
                    table.ForeignKey(
                        name: "fk_purchases_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_purchases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "visit_block_items",
                columns: table => new
                {
                    block_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_block_items", x => new { x.block_id, x.position });
                    table.ForeignKey(
                        name: "fk_visit_block_items_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_block_items_visit_blocks_block_id",
                        column: x => x.block_id,
                        principalTable: "visit_blocks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "visit_stops",
                columns: table => new
                {
                    visit_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    next_directions = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    prev_directions = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    override_image = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_visit_stops", x => new { x.visit_id, x.position });
                    table.CheckConstraint("visit_stops_position_nonnegative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_visit_stops_items_item_id",
                        column: x => x.item_id,
                        principalTable: "items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_visit_stops_visits_visit_id",
                        column: x => x.visit_id,
                        principalTable: "visits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_quiz_answers",
                columns: table => new
                {
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    question_index = table.Column<int>(type: "integer", nullable: false),
                    selected_index = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_quiz_answers", x => new { x.session_id, x.user_id, x.question_index });
                    table.CheckConstraint("session_quiz_answers_indexes_nonnegative", "question_index >= 0 AND selected_index >= 0");
                    table.ForeignKey(
                        name: "fk_session_quiz_answers_session_participants_session_id_user_id",
                        columns: x => new { x.session_id, x.user_id },
                        principalTable: "session_participants",
                        principalColumns: new[] { "session_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "section_responses",
                columns: table => new
                {
                    session_id = table.Column<long>(type: "bigint", nullable: false),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "text", nullable: true),
                    selected_index = table.Column<int>(type: "integer", nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_section_responses", x => new { x.session_id, x.question_id, x.user_id });
                    table.CheckConstraint("section_responses_one_answer", "num_nonnulls(text, selected_index) = 1");
                    table.CheckConstraint("section_responses_text_length", "char_length(text) <= 1000");
                    table.ForeignKey(
                        name: "fk_section_responses_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_section_responses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_section_responses_visit_block_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "visit_block_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contents_museum_id_floor",
                table: "contents",
                columns: new[] { "museum_id", "floor" });

            migrationBuilder.CreateIndex(
                name: "ix_contents_museum_id_type",
                table: "contents",
                columns: new[] { "museum_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_contents_universal_id",
                table: "contents",
                column: "universal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_associated_contents_content_id",
                table: "item_associated_contents",
                column: "content_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_content_id",
                table: "items",
                column: "content_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_creator_id",
                table: "items",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_museums_slug",
                table: "museums",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_points_of_interest_museum_id_floor",
                table: "points_of_interest",
                columns: new[] { "museum_id", "floor" });

            migrationBuilder.CreateIndex(
                name: "ix_purchases_item_id",
                table: "purchases",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_visits_visit_id",
                table: "saved_visits",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_section_responses_question_id",
                table: "section_responses",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_section_responses_user_id",
                table: "section_responses",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_activities_session_id",
                table: "session_activities",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_activities_user_id",
                table: "session_activities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_messages_session_id",
                table: "session_messages",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_messages_user_id",
                table: "session_messages",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_participants_user_id",
                table: "session_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_code",
                table: "sessions",
                column: "code",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_owner_id",
                table: "sessions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_visit_id",
                table: "sessions",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_block_items_item_id",
                table: "visit_block_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_visit_block_questions_block_id_position",
                table: "visit_block_questions",
                columns: new[] { "block_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_blocks_visit_id_position",
                table: "visit_blocks",
                columns: new[] { "visit_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_visit_stops_item_id",
                table: "visit_stops",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_creator_id",
                table: "visits",
                column: "creator_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_museum_id",
                table: "visits",
                column: "museum_id");

            migrationBuilder.CreateIndex(
                name: "ix_visits_slug",
                table: "visits",
                column: "slug");

            // ---- Raw SQL: what EF cannot model ----

            // Case-insensitive uniqueness; the stored value keeps the user's casing.
            // Queries must say lower(username) = lower(@x) for the planner to use them.
            migrationBuilder.Sql("CREATE UNIQUE INDEX users_username_lower_uq ON users (lower(username));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX users_email_lower_uq ON users (lower(email));");

            // updated_at is set by the database on every UPDATE, so EF, Dapper and raw SQL
            // writes all keep it right. EF reads the new value back with RETURNING.
            migrationBuilder.Sql("""
                CREATE FUNCTION set_updated_at() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.updated_at = now();
                    RETURN NEW;
                END;
                $$;
                """);
            foreach (var table in TablesWithUpdatedAt)
            {
                migrationBuilder.Sql($"""
                    CREATE TRIGGER {table}_set_updated_at BEFORE UPDATE ON {table}
                    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                    """);
            }
            // Visits: not for view_count, which changes on every read of the visit.
            migrationBuilder.Sql("""
                CREATE TRIGGER visits_set_updated_at
                BEFORE UPDATE OF museum_id, creator_id, title, slug, description, image_url, type, length, is_public
                ON visits
                FOR EACH ROW EXECUTE FUNCTION set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TablesWithUpdatedAt.Append("visits"))
            {
                migrationBuilder.Sql($"DROP TRIGGER {table}_set_updated_at ON {table};");
            }
            migrationBuilder.Sql("DROP FUNCTION set_updated_at();");
            migrationBuilder.Sql("DROP INDEX users_email_lower_uq;");
            migrationBuilder.Sql("DROP INDEX users_username_lower_uq;");
            migrationBuilder.DropTable(
                name: "item_associated_contents");

            migrationBuilder.DropTable(
                name: "item_texts");

            migrationBuilder.DropTable(
                name: "points_of_interest");

            migrationBuilder.DropTable(
                name: "purchases");

            migrationBuilder.DropTable(
                name: "saved_visits");

            migrationBuilder.DropTable(
                name: "section_responses");

            migrationBuilder.DropTable(
                name: "session_activities");

            migrationBuilder.DropTable(
                name: "session_messages");

            migrationBuilder.DropTable(
                name: "session_quiz_answers");

            migrationBuilder.DropTable(
                name: "visit_block_items");

            migrationBuilder.DropTable(
                name: "visit_quiz_questions");

            migrationBuilder.DropTable(
                name: "visit_stops");

            migrationBuilder.DropTable(
                name: "visit_block_questions");

            migrationBuilder.DropTable(
                name: "session_participants");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.DropTable(
                name: "visit_blocks");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "contents");

            migrationBuilder.DropTable(
                name: "visits");

            migrationBuilder.DropTable(
                name: "floor_plans");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "museums");
        }
    }
}
